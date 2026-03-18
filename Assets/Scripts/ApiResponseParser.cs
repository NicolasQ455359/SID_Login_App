using System.Collections.Generic;

public static class ApiResponseParser
{
    public static object Deserialize(string json)
    {
        return MiniJson.Json.Deserialize(json);
    }

    public static string Serialize(Dictionary<string, object> payload)
    {
        return MiniJson.Json.Serialize(payload);
    }
}
