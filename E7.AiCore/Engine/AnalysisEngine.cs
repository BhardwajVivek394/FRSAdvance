using System;

namespace E7.AiCore
{
    // The web host the engine talks back to. Only page streaming crosses this boundary: the engine never
    // touches Request/Response/Session itself.
    public interface IAnalysisHost
    {
        void WriteSse(string type, object data);
    }

    // AnalysisEngine -- the AI analysis logic of RDPMS (Shared AI Core 2e, 1.0.165.0).
    // One instance per request, owned by the calling controller, exactly like the controller it came from.
    public sealed partial class AnalysisEngine
    {
        private readonly IAnalysisHost _engineHost;

        public AnalysisEngine(IAnalysisHost host)
        {
            _engineHost = host;
        }

        // Same signature as the former AiChatController.WriteSse; the controller writes to the response.
        internal void WriteSse(string type, object data)
        {
            _engineHost.WriteSse(type, data);
        }
    }
}
