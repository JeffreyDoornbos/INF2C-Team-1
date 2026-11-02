using System.Text.Json.Nodes;
using CargoHub.Api.Domain;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace CargoHub.Api.Documentation;

// Keep presentation examples separate from controllers and database behavior.
public sealed class DemoOperationFilter : IOperationFilter
{
    private static readonly Dictionary<string, (string Label, Type Model)> Resources = new()
    {
        ["warehouses"] = ("Magazijnen", typeof(Warehouse)),
        ["locations"] = ("Locaties", typeof(Location)),
        ["items"] = ("Artikelen", typeof(Item)),
        ["inventories"] = ("Voorraad", typeof(Inventory)),
        ["item_groups"] = ("Artikelgroepen", typeof(ItemGroup)),
        ["item_lines"] = ("Artikellijnen", typeof(ItemLine)),
        ["item_types"] = ("Artikeltypen", typeof(ItemType)),
        ["suppliers"] = ("Leveranciers", typeof(Item))
    };

    public static string TagFor(string path)
    {
        var resource = path.Split('/').ElementAtOrDefault(2) ?? path;
        return Resources.TryGetValue(resource, out var entry) ? entry.Label : resource;
    }

    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var path = context.ApiDescription.RelativePath!.Split('?')[0];
        var segments = path.Split('/');
        var resource = segments.ElementAtOrDefault(2);
        if (resource is null || !Resources.TryGetValue(resource, out var entry)) return;
        var (label, model) = entry;
        var method = context.ApiDescription.HttpMethod!;
        var classification = resource is "item_groups" or "item_lines" or "item_types";
        var nested = segments.Length > 4 && resource != "inventories";
        var collection = segments.Length == 3;
        var obsoleteInventory = resource == "inventories" && segments.Length == 4;
        operation.OperationId = context.MethodInfo.Name;
        operation.Summary = $"{label}: " + (method switch
        {
            "POST" when resource == "inventories" => "voorraad toevoegen of bijwerken",
            "POST" => "aanmaken",
            "PUT" => "wijzigen",
            "DELETE" => "verwijderen",
            _ when obsoleteInventory => "oude ID-route (altijd 404)",
            _ when nested && path.EndsWith("/totals") => "voorraadtotalen berekenen",
            _ when nested && path.EndsWith("/items") => classification ? "gekoppelde artikel-ID's ophalen" : "artikelen ophalen",
            _ when nested && path.EndsWith("/locations") => "gekoppelde locaties ophalen",
            _ when nested => "voorraadregels ophalen",
            _ when collection => "lijst ophalen",
            _ => "één record ophalen"
        });
        operation.Description = "Voorbeelden zijn fictief en gebruiken snake_case-veldnamen. ";
        operation.Responses = new OpenApiResponses();

        foreach (var parameter in (operation.Parameters ?? []).OfType<OpenApiParameter>())
        {
            parameter.Example = JsonValue.Create(parameter.In == ParameterLocation.Path && method is "PUT" or "DELETE" ? 900001 : 1);
            parameter.Description = parameter.In == ParameterLocation.Query
                ? "Optioneel filter op dit ID. Meerdere filters worden gecombineerd (EN). Laat leeg om niet te filteren."
                : "ID van het record. Gebruik een bestaand ID uit de GET-lijst, of 900001 voor je eigen demorecord.";
        }

        if (obsoleteInventory)
        {
            operation.Deprecated = true;
            operation.Description += "Voorraad heeft geen los ID. Gebruik /api/v1/inventories/{itemId}/{locationId}.";
            AddResponse("404", "Deze oude route retourneert altijd 404.");
            return;
        }

        if (method == "GET")
        {
            Type responseType = model;
            JsonNode example = Sample(resource, false);
            if (nested && path.EndsWith("/totals"))
            {
                responseType = typeof(InventoryTotalsExample);
                example = JsonNode.Parse("""{"total_expected":3,"total_ordered":2,"total_allocated":1,"total_available":9}""")!;
                operation.Description += "total_available is de som van quantity_on_hand min quantity_allocated. ";
            }
            else if (nested && path.EndsWith("/items") && classification)
            {
                responseType = typeof(int[]);
                example = JsonNode.Parse("[1,2,3]")!;
                operation.Description += "Dit antwoord bevat alleen artikel-ID's. Een onbekende classificatie geeft een lege lijst. ";
            }
            else if (collection || nested)
            {
                var child = nested ? segments[^1] switch { "locations" => "locations", "inventory" => "inventories", _ => "items" } : resource;
                responseType = Resources[child].Model.MakeArrayType();
                example = new JsonArray(Sample(child, false));
                operation.Description += "Geen resultaten geeft een lege JSON-array. ";
            }
            AddResponse("200", classification && !collection && !nested
                ? "Record gevonden. Onbekend ID geeft ook 200 met JSON null."
                : "Gegevens opgehaald.", responseType, example);
            if (!collection && !classification && resource != "suppliers")
                AddResponse("404", "Het record bestaat niet.");
            if (operation.Parameters?.Any(parameter => parameter.In == ParameterLocation.Query) == true)
                AddResponse("400", "Een opgegeven filter moet een geheel getal zijn.", typeof(Microsoft.AspNetCore.Mvc.ValidationProblemDetails));
        }
        else
        {
            operation.Description += "Deze aanvraag wijzigt de aangesloten database. ";
            if (method is "POST" or "PUT")
            {
                var body = Sample(resource, true);
                if (method == "PUT")
                {
                    if (body["name"] is not null) body["name"] = "Demo gewijzigd";
                    if (body["description"] is not null) body["description"] = "Gewijzigd tijdens de presentatie";
                }
                foreach (var media in operation.RequestBody!.Content!.Values)
                    media.Example = body.DeepClone();
                operation.Description += "created_at en updated_at hoef je niet mee te sturen. De server beheert deze tijdstippen; PUT behoudt created_at. ";
                if (method == "PUT") operation.Description += "Het pad bepaalt het record. PUT vervangt de wijzigbare velden; stuur alle benodigde velden mee. ";
                if (resource == "items") operation.Description += "commodity_code is momenteel tekst: gebruik aanhalingstekens. item_group_id, item_line_id, item_type_id en supplier_id moeten bestaan. ";
                if (resource == "locations") operation.Description += "warehouse_id moet bestaan. ";
                if (resource == "inventories") operation.Description += "item_id en location_id moeten bestaan. Hoeveelheden zijn niet negatief en quantity_allocated mag niet groter zijn dan quantity_on_hand. POST werkt een bestaande combinatie bij. ";
                AddResponse("400", "Ongeldige JSON, verkeerd datatype of ontbrekende verplichte body/tekstvelden.", typeof(Microsoft.AspNetCore.Mvc.ValidationProblemDetails));
            }
            var status = method == "POST" ? "201" : "200";
            var returnsObject = !classification && method != "DELETE";
            AddResponse(status, returnsObject ? "Opgeslagen record." : "Geslaagd, lege responsebody.",
                returnsObject ? model : null, returnsObject ? Sample(resource, false) : null);
            if (classification)
            {
                if (method != "POST") operation.Description += "Een onbekend ID geeft 200 zonder wijziging. ";
                if (method is "POST" or "DELETE")
                    AddResponse("409", method == "POST" ? "Dit ID bestaat al." : "De classificatie wordt nog gebruikt door artikelen.", typeof(Microsoft.AspNetCore.Mvc.ProblemDetails));
            }
            else
            {
                if (method != "POST") AddResponse("404", "Het record bestaat niet.");
                AddResponse("500", "Huidige beperking: databaseconflicten of constraintfouten worden nog niet gecontroleerd afgehandeld.");
            }
        }

        void AddResponse(string code, string description, Type? type = null, JsonNode? example = null)
        {
            var response = new OpenApiResponse { Description = description };
            var problem = type is not null && typeof(Microsoft.AspNetCore.Mvc.ProblemDetails).IsAssignableFrom(type);
            if (problem)
            {
                example = new JsonObject { ["status"] = int.Parse(code), ["title"] = description };
                if (code == "400") example["errors"] = new JsonObject { ["$"] = new JsonArray("De JSON-body kan niet worden verwerkt.") };
            }
            if (type is not null)
                response.Content = new Dictionary<string, OpenApiMediaType>
                {
                    [problem ? "application/problem+json" : "application/json"] = new()
                    {
                        Schema = context.SchemaGenerator.GenerateSchema(type, context.SchemaRepository),
                        Example = example
                    }
                };
            operation.Responses[code] = response;
        }
    }

    private static JsonObject Sample(string resource, bool request)
    {
        var json = resource switch
        {
            "warehouses" => """{"id":900001,"code":"DEMO-WH","name":"Demo magazijn","address":"Voorbeeldstraat 1","city":"Rotterdam","zip_code":"3000AA","province":"Zuid-Holland","country":"NL","contact_name":"Demo contact","contact_phone":"0100000000","contact_email":"demo@example.com"}""",
            "locations" => """{"id":900001,"warehouse_id":1,"code":"DEMO-A-01","name":"Demo opslaglocatie"}""",
            "items" or "suppliers" => """{"id":900001,"code":"DEMO-ITEM","description":"Demo artikel","barcode":"1234567890123","model_number":"DEMO-01","commodity_code":"123456","unit_weight":1.5,"item_line_id":1,"item_group_id":1,"item_type_id":1,"min_purchase_qty":1,"case_size":1,"packaging_type":"Box","order_multiple":1,"supplier_id":1,"supplier_sku":"DEMO-SKU"}""",
            "inventories" => """{"item_id":900001,"location_id":900001,"quantity_on_hand":10,"quantity_expected":3,"quantity_ordered":2,"quantity_allocated":1}""",
            _ => """{"id":900001,"name":"Demo classificatie","description":"Aangemaakt tijdens de presentatie"}"""
        };
        var sample = JsonNode.Parse(json)!.AsObject();
        if (!request)
        {
            if (sample.ContainsKey("id")) sample["id"] = 1;
            if (resource == "inventories") { sample["item_id"] = 1; sample["location_id"] = 1; }
            sample["created_at"] = "2026-11-02T10:00:00Z";
            sample["updated_at"] = "2026-11-02T10:00:00Z";
        }
        return sample;
    }

    // Documents the anonymous object returned by ItemsController without changing it.
    private sealed record InventoryTotalsExample(int TotalExpected, int TotalOrdered, int TotalAllocated, int TotalAvailable);
}
