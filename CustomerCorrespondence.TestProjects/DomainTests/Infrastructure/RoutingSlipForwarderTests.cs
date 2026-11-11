using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using CustomerCorrespondence.Infrastructure.Messaging;
using Moq;
using NServiceBus;
using NServiceBus.Logging;
using Xunit;

namespace CustomerCorrespondenceTests.Infrastructure
{
    [TestClass]
    public class RoutingSlipForwarderTests
    {
        private class TestMessage { }

        private readonly Mock<IMessageHandlerContext> _contextMock;
        private readonly Dictionary<string, string> _headers;
        private readonly TestMessage _message;

        public RoutingSlipForwarderTests()
        {
            _headers = new Dictionary<string, string>();
            _contextMock = new Mock<IMessageHandlerContext>();
            _contextMock.SetupGet(c => c.MessageHeaders).Returns(_headers);
            _message = new TestMessage();
        }

        [TestMethod]
        public async Task ForwardToNextStepIfAny_NoRouteHeader_DoesNotSend()
        {
            await RoutingSlipForwarder.ForwardToNextStepIfAny(_contextMock.Object, _message);
            _contextMock.Verify(c => c.Send(It.IsAny<object>(), It.IsAny<SendOptions>()), Times.Never);
        }

        [TestMethod]
        public async Task ForwardToNextStepIfAny_InvalidRouteHeader_DoesNotSend()
        {
            _headers[RoutingSlipForwarder.RouteHeader] = "not a json";
            await RoutingSlipForwarder.ForwardToNextStepIfAny(_contextMock.Object, _message);
            _contextMock.Verify(c => c.Send(It.IsAny<object>(), It.IsAny<SendOptions>()), Times.Never);
        }

        [TestMethod]
        public async Task ForwardToNextStepIfAny_EmptyRoute_DoesNotSend()
        {
            _headers[RoutingSlipForwarder.RouteHeader] = JsonSerializer.Serialize(new List<string>());
            await RoutingSlipForwarder.ForwardToNextStepIfAny(_contextMock.Object, _message);
            _contextMock.Verify(c => c.Send(It.IsAny<object>(), It.IsAny<SendOptions>()), Times.Never);
        }

        //[TestMethod]
        //public async Task ForwardToNextStepIfAny_AtEndOfRoute_DoesNotSend()
        //{
        //    var route = new List<string> { "A", "B" };
        //    _headers[RoutingSlipForwarder.RouteHeader] = JsonSerializer.Serialize(route);
        //    _headers[RoutingSlipForwarder.IndexHeader] = "1";
        //    await RoutingSlipForwarder.ForwardToNextStepIfAny(_contextMock.Object, _message);
        //    _contextMock.Verify(c => c.Send(It.IsAny<object>(), It.IsAny<SendOptions>()), Times.Never);
        //}

        //[TestMethod]
        //public async Task ForwardToNextStepIfAny_ValidRoute_SendsToNextEndpoint()
        //{
        //    var route = new List<string> { "A", "B" };
        //    _headers[RoutingSlipForwarder.RouteHeader] = JsonSerializer.Serialize(route);
        //    _headers[RoutingSlipForwarder.IndexHeader] = "0";
        //    _headers[RoutingSlipForwarder.OriginatingIdHeader] = "orig-123";

        //    SendOptions? capturedOptions = null;
        //    _contextMock.Setup(c => c.Send(_message, It.IsAny<SendOptions>()))
        //        .Callback<object, SendOptions>((_, opts) => capturedOptions = opts)
        //        .Returns(Task.CompletedTask);

        //    await RoutingSlipForwarder.ForwardToNextStepIfAny(_contextMock.Object, _message);

        //    _contextMock.Verify(c => c.Send(_message, It.IsAny<SendOptions>()), Times.Once);
        //    Assert.IsNotNull(capturedOptions);
        //    Assert.AreNotEqual("B", capturedOptions.GetHeaders()[RoutingSlipForwarder.RouteHeader]);
        //    Assert.AreEqual("1", capturedOptions.GetHeaders()[RoutingSlipForwarder.IndexHeader]);
        //    Assert.AreEqual("orig-123", capturedOptions.GetHeaders()[RoutingSlipForwarder.OriginatingIdHeader]);
        //}

        //[TestMethod]
        //public async Task ForwardToNextStepIfAny_NextEndpointIsSameAsCurrent_DoesNotSend()
        //{
        //    var route = new List<string> { "A", "A" };
        //    _headers[RoutingSlipForwarder.RouteHeader] = JsonSerializer.Serialize(route);
        //    _headers[RoutingSlipForwarder.IndexHeader] = "0";
        //    await RoutingSlipForwarder.ForwardToNextStepIfAny(_contextMock.Object, _message);
        //    _contextMock.Verify(c => c.Send(It.IsAny<object>(), It.IsAny<SendOptions>()), Times.Never);
        //}
    }
}