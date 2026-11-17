using Microsoft.AspNetCore.Mvc.ApplicationModels;

namespace CargoHub.Api.Routing;

/// <summary>Publiceert dezelfde controllers onder de ingestelde API-versie, zonder de legacy-server te wijzigen.</summary>
public sealed class ApiVersionRouteConvention(string version) : IApplicationModelConvention
{
    public void Apply(ApplicationModel application)
    {
        foreach (var controller in application.Controllers)
        foreach (var selector in controller.Selectors.Concat(controller.Actions.SelectMany(action => action.Selectors)))
        {
            var route = selector.AttributeRouteModel;
            if (route?.Template is not { } template) continue;
            var prefix = template.StartsWith("~/", StringComparison.Ordinal) ? "~/"
                : template.StartsWith('/') ? "/" : "";
            var relative = template[prefix.Length..];
            if (relative.StartsWith("api/v1/", StringComparison.Ordinal) || relative == "api/v1")
                route.Template = prefix + "api/" + version + relative[6..];
        }
    }
}
