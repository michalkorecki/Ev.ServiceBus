﻿using System.Collections.Generic;
using System.Linq;
using Ev.ServiceBus.Abstractions;

namespace Ev.ServiceBus.IntegrationEvents.Subscription
{
    public class ServiceBusEventSubscriptionRegistry
    {
        private readonly Dictionary<string, MessageReceptionRegistration> _registrations;

        public ServiceBusEventSubscriptionRegistry(IEnumerable<MessageReceptionRegistration> registrations)
        {
            var regs = registrations.ToArray();

            var duplicatedHandlers = regs.GroupBy(o => new { o.Options.ClientType, o.Options.EntityPath, o.HandlerType }).Where(o => o.Count() > 1).ToArray();
            if (duplicatedHandlers.Any())
            {
                throw new DuplicateSubscriptionHandlerDeclarationException(duplicatedHandlers.SelectMany(o => o).ToArray());
            }

            var duplicateEvenTypeIds = regs.GroupBy(o => new {o.Options.ClientType, o.Options.EntityPath, o.EventTypeId}).Where(o => o.Count() > 1).ToArray();
            if (duplicateEvenTypeIds.Any())
            {
                throw new DuplicateEvenTypeIdDeclarationException(duplicateEvenTypeIds.SelectMany(o => o).ToArray());
            }

            _registrations = regs
                .ToDictionary(
                    o => ComputeKey(o.EventTypeId, o.Options.EntityPath, o.Options.ClientType),
                    o => o);
        }

        private string ComputeKey(string eventTypeId, string receiverName, ClientType clientType)
        {
            return $"{clientType}|{receiverName}|{eventTypeId}";
        }

        public MessageReceptionRegistration? GetRegistration(string eventTypeId, string receiverName, ClientType clientType)
        {
            if (_registrations.TryGetValue(ComputeKey(eventTypeId, receiverName, clientType), out var registrations))
            {
                return registrations;
            }

            return null;
        }

    }
}
