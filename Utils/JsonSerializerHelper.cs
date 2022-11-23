using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LanPlayServer.Utils
{
    public static class JsonSerializerHelper
    {
        public static JsonSerializerOptions GetDefaultSerializerOptions()
        {
            JsonSerializerOptions options = new JsonSerializerOptions
            {
                DictionaryKeyPolicy  = new SnakeCaseNamingPolicy(),
                PropertyNamingPolicy = new SnakeCaseNamingPolicy(),
                WriteIndented        = true,
                AllowTrailingCommas  = true,
                ReadCommentHandling  = JsonCommentHandling.Skip
            };

            options.Converters.Add(new JsonStringEnumConverter());

            return options;
        }

        public static string Serialize<TValue>(TValue obj)
        {
            return JsonSerializer.Serialize(obj, GetDefaultSerializerOptions());
        }

        private class SnakeCaseNamingPolicy : JsonNamingPolicy
        {
            public override string ConvertName(string name)
            {
                if (string.IsNullOrEmpty(name))
                {
                    return name;
                }

                StringBuilder builder = new StringBuilder();

                for (int i = 0; i < name.Length; i++)
                {
                    char c = name[i];

                    if (char.IsUpper(c))
                    {
                        if (i == 0 || char.IsUpper(name[i - 1]))
                        {
                            builder.Append(char.ToLowerInvariant(c));
                        }
                        else
                        {
                            builder.Append("_");
                            builder.Append(char.ToLowerInvariant(c));
                        }
                    }
                    else
                    {
                        builder.Append(c);
                    }
                }

                return builder.ToString();
            }
        }
    }
}