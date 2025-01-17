﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Azure.Messaging.ServiceBus;
using Ev.ServiceBus.Abstractions;
using Ev.ServiceBus.Abstractions.MessageReception;
using Ev.ServiceBus.Management;

namespace Ev.ServiceBus.Dispatch
{
    public class DispatchSender : IDispatchSender
    {
        private const int MaxMessagePerSend = 100;
        private readonly IMessagePayloadSerializer _messagePayloadSerializer;
        private readonly ServiceBusRegistry _dispatchRegistry;
        private readonly IServiceBusRegistry _registry;
        private readonly IMessageMetadataAccessor _messageMetadataAccessor;

        public DispatchSender(
            IServiceBusRegistry registry,
            IMessagePayloadSerializer messagePayloadSerializer,
            ServiceBusRegistry dispatchRegistry,
            IMessageMetadataAccessor messageMetadataAccessor)
        {
            _registry = registry;
            _messagePayloadSerializer = messagePayloadSerializer;
            _dispatchRegistry = dispatchRegistry;
            _messageMetadataAccessor = messageMetadataAccessor;
        }

        /// <inheritdoc />
        public async Task SendDispatches(IEnumerable<object> messagePayloads, CancellationToken token = default)
        {
            if (messagePayloads == null)
            {
                throw new ArgumentNullException(nameof(messagePayloads));
            }

            var dispatches = messagePayloads.Select(o => new Abstractions.Dispatch(o)).ToArray();
            await SendDispatches(dispatches, token);
        }

        /// <inheritdoc />
        public async Task SendDispatches(IEnumerable<Abstractions.Dispatch> messagePayloads, CancellationToken token = default)
        {
            if (messagePayloads == null)
            {
                throw new ArgumentNullException(nameof(messagePayloads));
            }

            var dispatches = CreateMessagesToSend(messagePayloads);

            await PaginateAndSendMessages(dispatches, async (sender, page) =>
            {
                await sender.SendMessagesAsync(page, token).ConfigureAwait(false);
            });
        }

        /// <inheritdoc />
        public async Task ScheduleDispatches(IEnumerable<object> messagePayloads, DateTimeOffset scheduledEnqueueTime, CancellationToken token = default)
        {
            if (messagePayloads == null)
            {
                throw new ArgumentNullException(nameof(messagePayloads));
            }

            var dispatches = messagePayloads.Select(o => new Abstractions.Dispatch(o)).ToArray();
            await ScheduleDispatches(dispatches, scheduledEnqueueTime, token);
        }

        /// <inheritdoc />
        public async Task ScheduleDispatches(IEnumerable<Abstractions.Dispatch> messagePayloads, DateTimeOffset scheduledEnqueueTime, CancellationToken token = default)
        {
            if (messagePayloads == null)
            {
                throw new ArgumentNullException(nameof(messagePayloads));
            }

            var dispatches = CreateMessagesToSend(messagePayloads);
            await PaginateAndSendMessages(dispatches, async (sender, page) =>
            {
                await sender.ScheduleMessagesAsync(page, scheduledEnqueueTime, token).ConfigureAwait(false);
            });
        }

        private async Task PaginateAndSendMessages(MessageToSend[] dispatches, Func<IMessageSender, IEnumerable<ServiceBusMessage>, Task> senderAction)
        {
            foreach (var groupedDispatch in dispatches
                         .GroupBy(o => new { o.Registration.Options.ClientType, o.Registration.Options.ResourceId }))
            {
                var sender = groupedDispatch.Key.ClientType == ClientType.Queue
                    ? _registry.GetQueueSender(groupedDispatch.Key.ResourceId)
                    : _registry.GetTopicSender(groupedDispatch.Key.ResourceId);

                var paginatedMessages = groupedDispatch.Select(o => o.Message)
                    .Select((x, i) => new
                    {
                        Item = x,
                        Index = i
                    })
                    .GroupBy(x => x.Index / MaxMessagePerSend, x => x.Item);

                foreach (var pageMessages in paginatedMessages)
                {
                    await senderAction.Invoke(sender, pageMessages.Select(m => m).ToArray());
                }
            }
        }

        private class MessageToSend
        {
            public MessageToSend(ServiceBusMessage message, MessageDispatchRegistration registration)
            {
                Message = message;
                Registration = registration;
            }

            public ServiceBusMessage Message { get; }
            public MessageDispatchRegistration Registration { get; }
        }

        private MessageToSend[] CreateMessagesToSend(IEnumerable<Abstractions.Dispatch> messagePayloads)
        {
            var dispatches =
                (
                    from dispatch in messagePayloads
                    // the same dispatch can be published to several senders
                    let registrations = _dispatchRegistry.GetDispatchRegistrations(dispatch.Payload.GetType())
                    from eventPublicationRegistration in registrations
                    let message = CreateMessage(eventPublicationRegistration, dispatch)
                    select new MessageToSend(message, eventPublicationRegistration)
                )
                .ToArray();
            return dispatches;
        }

        private ServiceBusMessage CreateMessage(
            MessageDispatchRegistration registration,
            Abstractions.Dispatch dispatch)
        {
            var originalCorrelationId = _messageMetadataAccessor.Metadata?.CorrelationId ?? Guid.NewGuid().ToString();
            var result = _messagePayloadSerializer.SerializeBody(dispatch.Payload);
            var message = MessageHelper.CreateMessage(result.ContentType, result.Body, registration.PayloadTypeId);

            message.SessionId = dispatch.SessionId;
            message.CorrelationId = dispatch.CorrelationId ?? originalCorrelationId;

            foreach (var customizer in registration.OutgoingMessageCustomizers)
            {
                customizer?.Invoke(message, dispatch.Payload);
            }

            return message;
        }
    }
}
