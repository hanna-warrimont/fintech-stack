using System.Text.Json;

namespace Portal.Messaging.Contracts;

internal static class StrictJson
{
    // Streng: fehlende Felder oder null in Nicht-null-Feldern sind ein Fehler – sonst würde aus {"payload":{}}
    // ein Objekt voller Defaults, das beim Speichern scheitert (Partition hängt) oder Consumer abstürzen lässt.
    public static readonly JsonSerializerOptions Options = new(JsonSerializerOptions.Web)
    {
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
    };
}
