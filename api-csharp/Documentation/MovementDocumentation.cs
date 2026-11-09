using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace CargoHub.Api.Documentation;

// Enrich Anmol's controllers without replacing their DTO schemas or response annotations.
internal static class MovementDocumentation
{
    public static void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        operation.Responses ??= new OpenApiResponses();
        var path = context.ApiDescription.RelativePath!.Split('?')[0];
        var method = context.ApiDescription.HttpMethod!;
        var shipment = path.StartsWith("api/v1/shipments");
        var commit = path.EndsWith("/commit");
        var items = path.EndsWith("/items");
        var orders = path.EndsWith("/orders");
        var collection = path.Split('/').Length == 3;
        operation.OperationId = context.MethodInfo.DeclaringType!.Name + "_" + context.MethodInfo.Name;
        operation.Summary = (shipment ? "Zendingen: " : "Transfers: ") + (method switch
        {
            "POST" => "aanmaken",
            "DELETE" => "verwijderen",
            "PUT" when commit => "verwerken en voorraad verplaatsen",
            "PUT" when items => "artikelregels vervangen",
            "PUT" when orders => "gekoppelde order instellen",
            "PUT" => "wijzigen",
            _ when items => "artikelregels ophalen",
            _ when orders => "gekoppelde order-ID's ophalen",
            _ when collection => "lijst met artikelregels ophalen",
            _ => "één record met artikelregels ophalen"
        });
        operation.Description += "Voorbeelden zijn fictief. Gebruik bestaande artikel-, locatie- en order-ID's uit je database. ";
        foreach (var parameter in (operation.Parameters ?? []).OfType<OpenApiParameter>())
        {
            parameter.Example = JsonValue.Create(method == "GET" ? 1 : 900001);
            parameter.Description = "Record-ID; gebruik 900001 voor het eigen demorecord, als dat ID nog vrij is.";
        }
        if (method == "GET")
        {
            operation.Description += collection || items || orders
                ? "Geen resultaten geeft een lege lijst. " : "Een onbekend ID geeft 200 met JSON null. ";
            return;
        }

        operation.Description += "Deze aanvraag wijzigt de aangesloten database. Succes geeft een lege responsebody. ";
        if (commit)
            operation.Description += "Verplaatst beschikbare voorraad transactioneel van bron naar bestemming en zet de status op Processed. Herhalen verplaatst geen tweede keer voorraad. Onvoldoende beschikbare voorraad geeft 409. Een verwerkte transfer kan niet meer worden gewijzigd of verwijderd. ";
        else if (!shipment)
            operation.Description += "Controleer vóór commit de beschikbare voorraad op de bronlocatie. Het voorbeeld gebruikt artikel 119 op locatie 124 uit de oude importdata. Bron en bestemming moeten bestaan en verschillen. Artikelregels moeten uniek zijn en een positief geheel aantal hebben. POST maakt de status Scheduled; gebruik commit om te verwerken. PUT met items=null behoudt de regels. Een onbekend ID bij PUT of DELETE geeft 200 zonder wijziging. ";
        else if (orders)
            operation.Description += "Stuur een lijst order-ID's. De eerste order wordt gekoppeld; [] verwijdert de koppeling. Een onbekende zending geeft 200 zonder wijziging. ";
        else if (items)
            operation.Description += "Vervangt alle artikelregels en past quantity_ordered aan volgens het verschil. Een onbekende zending geeft 400. ";
        else
            operation.Description += "shipment_date vereist een datum of een tijdstip met Z/UTC-offset. POST/PUT beheert timestamps op de server. PUT met items=null behoudt de regels; [] wist ze. Een onbekend ID bij PUT of DELETE geeft 200 zonder wijziging. ";

        if (operation.RequestBody?.Content is { } content)
        {
            var example = JsonNode.Parse(orders ? "[1]" : items ? """[{"item_id":1,"amount":1}]"""
                : shipment ? """{"id":900001,"reference":"DEMO-SHIPMENT","order_id":1,"shipment_date":"2026-11-09T10:00:00Z","shipment_type":"Standard","shipment_status":"Scheduled","carrier_name":"Demo vervoerder","shipping_method":"Road","payment_type":"Prepaid","items":[{"item_id":1,"amount":1}]}"""
                : """{"id":900001,"reference":"DEMO-TRANSFER","from_location_id":124,"to_location_id":1,"transfer_status":"Scheduled","items":[{"item_id":119,"amount":1}]}""");
            foreach (var media in content.Values) media.Example = example!.DeepClone();
        }

        Describe(method == "POST" ? "201" : "200", "Geslaagd, lege responsebody.");
        Describe("400", "Ongeldige invoer, onbekende verwijzing of overtreden databaseregel.", true);
        Describe("409", shipment ? "Dit ID bestaat al." : "Dit ID bestaat al, onvoldoende voorraad of een statusconflict.", true);
        if (commit) Describe("404", "De transfer bestaat niet.", true);

        void Describe(string code, string description, bool problem = false)
        {
            if (!operation.Responses.TryGetValue(code, out var response))
            {
                var created = new OpenApiResponse { Description = description };
                if (problem) created.Content = new Dictionary<string, OpenApiMediaType>
                {
                    ["application/problem+json"] = new() { Schema = context.SchemaGenerator.GenerateSchema(typeof(ProblemDetails), context.SchemaRepository) }
                };
                operation.Responses[code] = created;
            }
            else if (response is OpenApiResponse concrete) concrete.Description = description;
        }
    }
}
