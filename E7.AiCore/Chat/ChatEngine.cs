using Domain;
using System;

namespace E7.AiCore
{
    // ChatEngine -- the ChatBot ("AI Operations Assistant") logic in the Shared AI Core (3a, 1.2.10.0).
    // One instance per request, owned by ChatBotController; streams back through IAnalysisHost.WriteSse,
    // the same bridge AnalysisEngine uses.
    public sealed partial class ChatEngine
    {
        private readonly IAnalysisHost _engineHost;

        public ChatEngine(IAnalysisHost host)
        {
            _engineHost = host;
        }

        // Same signature as the former ChatBotController.WriteSse; the controller writes to the response.
        internal void WriteSse(string type, object data)
        {
            _engineHost.WriteSse(type, data);
        }
    }
}
