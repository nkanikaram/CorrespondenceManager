using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CustomerCorrespondence.Domain.Entities.Messages
{
    // PSEUDOCODE / PLAN
    // 1. Keep existing message classes as simple DTOs implementing ICommand.
    // 2. If RequestCompositionHandlerMessage is not being invoked, the likely causes are:
    //    - It is not being sent/published on the bus, or
    //    - There is no handler registered for ICommand / this specific message.
    // 3. Ensure this message type is distinct and discoverable:
    //    - Keep a parameterless constructor if frameworks (e.g., DI / bus) need it.
    //    - Keep strongly-typed property for SequentialProcess, with required keyword.
    // 4. Optionally add a convenience constructor to ensure Process is always provided
    //    when created in code, but not required by the bus (keep default ctor public).
    // 5. No behavioral logic is added here; wiring up the handler is done elsewhere
    //    (handler registration / routing configuration). This file only defines messages.

    public class BeginRequestHandlerMessage : ICommand
    {
        public required SequentialProcess Process { get; set; }

        public BeginRequestHandlerMessage() { }

        public BeginRequestHandlerMessage(SequentialProcess process)
        {
            Process = process ?? throw new ArgumentNullException(nameof(process));
        }
    }

    public class AssignDocumentumIdHandlerMessage : ICommand
    {
        public required SequentialProcess Process { get; set; }

        public AssignDocumentumIdHandlerMessage() { }

        public AssignDocumentumIdHandlerMessage(SequentialProcess process)
        {
            Process = process ?? throw new ArgumentNullException(nameof(process));
        }
    }

    public class AddCorrespondenceSBusHandlerMessage : ICommand
    {
        public required SequentialProcess Process { get; set; }

        public AddCorrespondenceSBusHandlerMessage() { }

        public AddCorrespondenceSBusHandlerMessage(SequentialProcess process)
        {
            Process = process ?? throw new ArgumentNullException(nameof(process));
        }
    }

    public class AddCorrespondenceDBHandlerMessage : ICommand
    {
        public required SequentialProcess Process { get; set; }

        public AddCorrespondenceDBHandlerMessage() { }

        public AddCorrespondenceDBHandlerMessage(SequentialProcess process)
        {
            Process = process ?? throw new ArgumentNullException(nameof(process));
        }
    }

    public class RequestCompositionHandlerMessage : ICommand
    {
        // This is the message payload used by the RequestComposition handler.
        // If the handler is not invoking, verify:
        //  - This type is used when sending the command (e.g., bus.Send<RequestCompositionHandlerMessage>(...))
        //  - The corresponding handler is registered to handle RequestCompositionHandlerMessage
        //  - Any routing configuration for ICommand / this message type includes this message.
        public required SequentialProcess Process { get; set; }

        public RequestCompositionHandlerMessage() { }

        public RequestCompositionHandlerMessage(SequentialProcess process)
        {
            Process = process ?? throw new ArgumentNullException(nameof(process));
        }
    }

    public class UpdateCorrespondenceDBHandlerMessage : ICommand
    {
        public required SequentialProcess Process { get; set; }

        public UpdateCorrespondenceDBHandlerMessage() { }

        public UpdateCorrespondenceDBHandlerMessage(SequentialProcess process)
        {
            Process = process ?? throw new ArgumentNullException(nameof(process));
        }
    }

    public class UpdateArchivedStatusDBHandlerMessage : ICommand
    {
        public required SequentialProcess Process { get; set; }

        public UpdateArchivedStatusDBHandlerMessage() { }

        public UpdateArchivedStatusDBHandlerMessage(SequentialProcess process)
        {
            Process = process ?? throw new ArgumentNullException(nameof(process));
        }
    }
}
