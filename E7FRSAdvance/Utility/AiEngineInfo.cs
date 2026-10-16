namespace E7FRSAdvance.AiCore
{
    // 1.0.193.0: one row of AiModelConfig/GetAllWithKeys.
    // Key is a SECRET: server side only -- never serialise this class to a browser, never log it.
    public sealed class AiEngineInfo
    {
        public string EngineCode { get; set; }

        public string DisplayName { get; set; }

        public string Provider { get; set; }

        public string ModelName { get; set; }

        public string Key { get; set; }

        public bool IsDefault { get; set; }

        /// <summary>Provider normalised to deepseek | openai | anthropic, or null if unknown.</summary>
        public string ProviderKind
        {
            get
            {
                string kind = (Provider ?? "").Trim().ToLowerInvariant();
                if (kind != "deepseek" && kind != "openai" && kind != "anthropic")
                {
                    return null;
                }
                return kind;
            }
        }
    }
}
