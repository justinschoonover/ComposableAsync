﻿using System;
using System.Threading;
using System.Threading.Tasks;
using ComposableAsync.Factory.Test.TestInfra.DummyClass;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace ComposableAsync.Concurrent.Test
{
    public class ActorFactoryBuilderTest : IDisposable
    {
        private readonly ActorFactoryBuilder _ActorFactoryBuilder;
        private readonly ICancellableDispatcher _CancellableDispatcher;
        private readonly ICancellableDispatcher _CancellableDispatcher2;
        private readonly ICancellableDispatcher _CancellableDispatcher3;

        public ActorFactoryBuilderTest()
        {
            _ActorFactoryBuilder = new ActorFactoryBuilder();
            _CancellableDispatcher = Substitute.For<ICancellableDispatcher, IAsyncDisposable>();
            _CancellableDispatcher2 = Substitute.For<ICancellableDispatcher, IAsyncDisposable>();
            _CancellableDispatcher3 = Substitute.For<ICancellableDispatcher, IAsyncDisposable>();
        }

        public void Dispose()
        {
            SynchronizationContext.SetSynchronizationContext(null);
        }

        [Fact]
        public async Task GetFactoryForFiber_Creates_Actor_Using_The_Provided_Fiber()
        {
            var fiber = Fiber.CreateMonoThreadedFiber();
            var fiberThread = await fiber.Enqueue(() => Thread.CurrentThread);

            var factory = _ActorFactoryBuilder.GetActorFactoryFrom(fiber);

            var target = new DummyClass();
            var actor = factory.Build<IDummyInterface2>(target);
            await actor.DoAsync();

            target.CallingThread.Should().Be(fiberThread);

            await fiber.DisposeAsync();
        }
    }
}
