﻿using System;
using System.Threading;
using System.Threading.Tasks;
using Ev.ServiceBus.Abstractions;
using Ev.ServiceBus.IntegrationEvents.Subscription;
using Ev.ServiceBus.IntegrationEvents.UnitTests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Ev.ServiceBus.IntegrationEvents.UnitTests
{
    public class SubscriptionConfigurationTest
    {
        [Fact]
        public void EventTypeIdMustBeSet()
        {
            var services = new ServiceCollection();
            Assert.Throws<ArgumentNullException>(
                () =>
                {
                    services.RegisterServiceBusReception()
                        .FromSubscription(
                            "topic",
                            "sub",
                            builder =>
                            {
                                builder.RegisterReception<SubscribedEvent, SubscribedEventHandler>()
                                    .CustomizeEventTypeId(null);
                            });

                });
        }

        [Fact]
        public void EventTypeIdIsAutoGenerated()
        {
            var services = new ServiceCollection();
            services.RegisterServiceBusReception()
                .FromSubscription(
                    "topic",
                    "sub",
                    builder =>
                    {
                        var reg = builder.RegisterReception<SubscribedEvent, SubscribedEventHandler>();
                        reg.EventTypeId.Should().Be("SubscribedEvent");
                    });
        }

        [Fact]
        public async Task HandlerCannotReceiveFromTheSameSubscriptionTwice()
        {
            var composer = new Composer();

            composer.WithAdditionalServices(services =>
            {
                services.RegisterServiceBusReception()
                    .FromSubscription(
                        "topicName",
                        "subscriptionName",
                        builder =>
                        {
                            builder.RegisterReception<SubscribedEvent, SubscribedEventHandler>();
                            builder.RegisterReception<SubscribedEvent, SubscribedEventHandler>();
                        });
            });

            await composer.Compose();

            var exception = Assert.Throws<DuplicateSubscriptionHandlerDeclarationException>(() =>
            {
                composer.Provider.GetService(typeof(ServiceBusEventSubscriptionRegistry));
            });
            exception.Message.Should().NotBeNull();
            exception.Duplicates.Should().SatisfyRespectively(
                ev =>
                {
                    ev.HandlerType.Should().Be(typeof(SubscribedEventHandler));
                    ev.Options.ClientType.Should().Be(ClientType.Subscription);
                    ev.EventTypeId.Should().Be("SubscribedEvent");
                },
                ev =>
                {
                    ev.HandlerType.Should().Be(typeof(SubscribedEventHandler));
                    ev.Options.ClientType.Should().Be(ClientType.Subscription);
                    ev.EventTypeId.Should().Be("SubscribedEvent");
                });
        }

        [Fact]
        public async Task HandlerCannotReceiveFromTheSameQueueTwice()
        {
            var composer = new Composer();

            composer.WithAdditionalServices(services =>
            {
                services.RegisterServiceBusReception()
                    .FromQueue(
                        "queueName",
                        builder =>
                        {
                            builder.RegisterReception<SubscribedEvent, SubscribedEventHandler>();
                            builder.RegisterReception<SubscribedEvent, SubscribedEventHandler>();
                        });
            });

            await composer.Compose();

            var exception = Assert.Throws<DuplicateSubscriptionHandlerDeclarationException>(() =>
            {
                composer.Provider.GetService(typeof(ServiceBusEventSubscriptionRegistry));
            });
            exception.Message.Should().NotBeNull();
            exception.Duplicates.Should().SatisfyRespectively(
                ev =>
                {
                    ev.HandlerType.Should().Be(typeof(SubscribedEventHandler));
                    ev.Options.ClientType.Should().Be(ClientType.Queue);
                    ev.EventTypeId.Should().Be("SubscribedEvent");
                },
                ev =>
                {
                    ev.HandlerType.Should().Be(typeof(SubscribedEventHandler));
                    ev.Options.ClientType.Should().Be(ClientType.Queue);
                    ev.EventTypeId.Should().Be("SubscribedEvent");
                });
        }

        [Fact]
        public async Task EventTypeIdCannotBeSetTwice()
        {
            var composer = new Composer();

            composer.WithAdditionalServices(services =>
            {
                services.RegisterServiceBusReception()
                    .FromQueue(
                        "queueName",
                        builder =>
                        {
                            builder.RegisterReception<SubscribedEvent, SubscribedEventHandler>()
                                .CustomizeEventTypeId("testEvent");
                            builder.RegisterReception<SubscribedEvent, SubscribedEventHandler2>()
                                .CustomizeEventTypeId("testEvent");
                        });

            });

            await composer.Compose();

            var exception = Assert.Throws<DuplicateEvenTypeIdDeclarationException>(() =>
            {
                composer.Provider.GetService(typeof(ServiceBusEventSubscriptionRegistry));
            });
            exception.Message.Should().NotBeNull();
            exception.Duplicates.Should().SatisfyRespectively(
                ev =>
                {
                    ev.HandlerType.Should().Be(typeof(SubscribedEventHandler));
                    ev.Options.ClientType.Should().Be(ClientType.Queue);
                    ev.EventTypeId.Should().Be("testEvent");
                },
                ev =>
                {
                    ev.HandlerType.Should().Be(typeof(SubscribedEventHandler2));
                    ev.Options.ClientType.Should().Be(ClientType.Queue);
                    ev.EventTypeId.Should().Be("testEvent");
                });
        }

        [Fact]
        public void ReceiveFromQueue_ArgumentCannotBeNull()
        {
            var services = new ServiceCollection();

            services.AddIntegrationEventHandling<BodyParser>();

            Assert.Throws<ArgumentNullException>(() =>
            {
                services.RegisterServiceBusReception().FromQueue(null, builder => {});
            });
        }

        [Theory]
        [InlineData(null, "subscriptionName")]
        [InlineData("topicName", null)]
        public void ReceiveFromSubscription_ArgumentCannotBeNull(string topicName, string subscriptionName)
        {
            var services = new ServiceCollection();

            services.AddIntegrationEventHandling<BodyParser>();

            Assert.Throws<ArgumentNullException>(() =>
            {
                services.RegisterServiceBusReception().FromSubscription(topicName, subscriptionName, builder => {});
            });
        }

        public class SubscribedEvent { }

        public class SubscribedEventHandler : IIntegrationEventHandler<SubscribedEvent>
        {
            public Task Handle(SubscribedEvent @event, CancellationToken cancellationToken)
            {
                return Task.CompletedTask;
            }
        }
        public class SubscribedEventHandler2 : IIntegrationEventHandler<SubscribedEvent>
        {
            public Task Handle(SubscribedEvent @event, CancellationToken cancellationToken)
            {
                return Task.CompletedTask;
            }
        }

    }
}
