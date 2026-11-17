#!/usr/bin/env bash
# Build a versioned, credential-free Linux release for the SSH/Compose deployment.
set -euo pipefail
version=${1:?Usage: build-release.sh 2.0.RUN.ATTEMPT OUTPUT_DIRECTORY}
output=${2:?Provide an output directory}
[[ $version =~ ^2\.[0-9]+\.[0-9]+\.[0-9]+$ ]] || { echo 'Invalid release version' >&2; exit 1; }
root=$(cd "$(dirname "$0")/../.." && pwd)
mkdir -p "$output"
output=$(cd "$output" && pwd)
stage=$(mktemp -d)
trap 'rm -rf "$stage"' EXIT
cd "$root"
dotnet tool restore
dotnet publish api-csharp/CargoHub.Api.csproj -c Release -r linux-x64 --self-contained false \
    -p:Version="$version" -o "$stage/app"
dotnet ef migrations script --idempotent --project api-csharp --configuration Release \
    --output "$stage/migrations.sql"
# Generate current data against the current entities without rewriting tracked SQL.
python3 - "$root" "$stage" "$version" <<'PY'
import hashlib, importlib.util, json, pathlib, subprocess, sys, zipfile
root, stage, version = pathlib.Path(sys.argv[1]), pathlib.Path(sys.argv[2]), sys.argv[3]
spec = importlib.util.spec_from_file_location('legacy_import', root/'database/postgresql/generate_legacy_import.py')
module = importlib.util.module_from_spec(spec); spec.loader.exec_module(module)
module.OUTPUT = stage
module.main()
# Local development settings and diagnostic symbols have no purpose on the VPS.
(stage/'app/appsettings.Development.json').unlink(missing_ok=True)
for path in (stage/'app').glob('*.pdb'): path.unlink()
manifest = {'schema_version': 1, 'version': version,
    'commit': subprocess.check_output(['git','rev-parse','HEAD'], cwd=root, text=True).strip(),
    'api_version': 'v2', 'runtime': 'linux-x64', 'files': {}}
for path in sorted(stage.rglob('*')):
    if path.is_file(): manifest['files'][path.relative_to(stage).as_posix()] = hashlib.sha256(path.read_bytes()).hexdigest()
(stage/'manifest.json').write_text(json.dumps(manifest, indent=2)+'\n')
PY
python3 - "$stage" "$output" "$version" <<'PY'
import hashlib,pathlib,sys,zipfile
stage,output,version=pathlib.Path(sys.argv[1]),pathlib.Path(sys.argv[2]),sys.argv[3]
archive=output/f'CargoHub_API_version_{version}.zip'
with zipfile.ZipFile(archive,'w',zipfile.ZIP_DEFLATED) as bundle:
    for path in sorted(stage.rglob('*')):
        if path.is_file(): bundle.write(path,path.relative_to(stage))
archive.with_suffix('.zip.sha256').write_text(hashlib.sha256(archive.read_bytes()).hexdigest()+'  '+archive.name+'\n')
print(archive)
PY
