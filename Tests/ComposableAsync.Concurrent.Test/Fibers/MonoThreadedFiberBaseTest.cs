﻿using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AutoFixture.Xunit2;
using ComposableAsync.Concurrent.Collections;
using ComposableAsync.Concurrent.WorkItems;
using ComposableAsync.Test.Helper;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace ComposableAsync.Concurrent.Test.Fibers
{
    public abstract class MonoThreadedFiberBaseTest : IAsyncLifetime
    {
        protected Thread RunningThread;
        private readonly ComposableAsyncDisposable _Disposables = new ComposableAsyncDisposable();
        private readonly ITestOutputHelper _TestOutputHelper;

        protected MonoThreadedFiberBaseTest(ITestOutputHelper testOutputHelper)
        {
            _TestOutputHelper = testOutputHelper;
            RunningThread = null;
        }

        public Task InitializeAsync() => Task.CompletedTask;

        public Task DisposeAsync() => _Disposables.DisposeAsync();

        protected abstract IMonoThreadFiber GetFiber(Action<Thread> onCreate = null);

        protected abstract IMonoThreadFiber GetFiber(IMpScQueue<IWorkItem> queue);

        protected IMonoThreadFiber GetSafeFiber(Action<Thread> onCreate = null)
        {
            var fiber = GetFiber(onCreate);
            return _Disposables.Add(fiber);
        }

        protected IMonoThreadFiber GetSafeFiber(IMpScQueue<IWorkItem> queue)
        {
            var fiber = GetFiber(queue);
            return _Disposables.Add(fiber);
        }

        protected Task TaskFactory(int sleep = 1)
        {
            RunningThread = Thread.CurrentThread;
            Thread.Sleep(sleep * 1000);
            return Task.CompletedTask;
        }

        protected Task<T> TaskFactory<T>(T result, int sleep = 1)
        {
            RunningThread = Thread.CurrentThread;
            Thread.Sleep(sleep * 1000);
            return Task.FromResult(result);
        }

        protected Task Throw()
        {
            throw new Exception();
        }

        protected Task<T> Throw<T>()
        {
            throw new Exception();
        }

        [Fact]
        public async Task Enqueue_Task_Should_Run_OnSeparatedThread()
        {
            var current = Thread.CurrentThread;
            //arrange
            var target = GetSafeFiber();

            //act
            await target.Enqueue(() => TaskFactory());

            //assert
            RunningThread.Should().NotBeNull();
            RunningThread.Should().NotBe(current);
        }

        [Fact]
        public async Task Enqueue_Task_Should_Run_OnSameThread()
        {
            //arrange
            var target = GetSafeFiber();

            //act
            await target.Enqueue(() => TaskFactory());
            var first = RunningThread;
            RunningThread = null;
            await target.Enqueue(() => TaskFactory());

            //assert
            RunningThread.Should().Be(first);
        }

        [Fact]
        public async Task Thread_Returns_Running_Thread()
        {
            //arrange
            var target = GetSafeFiber();

            //act
            await target.Enqueue(() => TaskFactory());

            target.Thread.Should().Be(RunningThread);
        }


        [Fact]
        public async Task Enqueue_Task_With_Cancellation_Should_Run_OnSameThread()
        {
            //arrange
            var target = GetSafeFiber();

            //act
            await target.Enqueue(() => TaskFactory(), CancellationToken.None);
            var first = RunningThread;
            RunningThread = null;
            await target.Enqueue(() => TaskFactory(), CancellationToken.None);

            //assert
            RunningThread.Should().Be(first);
        }

        [Fact]
        public async Task Enqueue_Task_With_Cancellation_Should_Run_OnSeparatedThread()
        {
            var current = Thread.CurrentThread;
            //arrange
            var target = GetSafeFiber();

            //act
            await target.Enqueue(() => TaskFactory(), CancellationToken.None);

            //assert
            RunningThread.Should().NotBeNull();
            RunningThread.Should().NotBe(current);
        }

        [Fact]
        public async Task Enqueue_Task_With_Cancellation_Immediately_Cancel_Tasks_Enqueued()
        {
            var target = GetSafeFiber();
            var tester = new TaskEnqueueWithCancellationTester(target);

            await tester.RunAndCancelTask();

            tester.TimeSpentToCancelTask.Should().BeLessThan(TimeSpan.FromSeconds(0.5));
        }

        [Fact]
        public async Task Enqueue_Task_With_Cancellation_Do_Not_Run_Task_Cancelled_Enqueued()
        {
            var target = GetSafeFiber();
            var tester = new TaskEnqueueWithCancellationTester(target);

            await tester.RunAndCancelTask();

            tester.CancelledTaskHasBeenExecuted.Should().BeFalse();
        }

        [Fact]
        public async Task Enqueue_Task_With_Cancellation_Do_Not_Cancel_Running_Task()
        {
            var target = GetSafeFiber();
            var tester = new TaskEnqueueWithCancellationTester(target);

            var returnedTask = tester.CancelNotCancellableRunningTask();
            await returnedTask;

            returnedTask.Status.Should().Be(TaskStatus.RanToCompletion);
        }

        [Fact]
        public async Task Enqueue_Task_With_Cancellation_Interpret_CanceledOperationException_As_Cancelled_Task()
        {
            var target = GetSafeFiber();
            var tester = new TaskEnqueueWithCancellationTester(target);

            var cancelledTask = tester.CancelCancellableRunningTask_T();
            var exception = await TaskEnqueueWithCancellationTester.AwaitForException(cancelledTask);

            exception.Should().NotBeNull();
            exception.Should().BeAssignableTo<TaskCanceledException>();
            cancelledTask.Status.Should().Be(TaskStatus.Canceled);
        }

        [Fact]
        public async Task Enqueue_after_await_does_not_continue_on_actor_thread()
        {
            //arrange
            var target = GetSafeFiber();

            //act
            var thread = await target.Enqueue(() => Thread.CurrentThread);

            thread.Should().NotBe(Thread.CurrentThread);
        }

        [Fact]
        public async Task Dispatch_And_Enqueue_Should_Run_OnSameThread()
        {
            //arrange
            var target = GetSafeFiber();

            //act
            await target.Enqueue(() => TaskFactory());
            var first = RunningThread;
            RunningThread = null;
            target.Dispatch(() => { RunningThread = Thread.CurrentThread; });
            await Task.Delay(200);

            //assert
            RunningThread.Should().Be(first);
        }

        [Fact]
        public async Task Enqueue_Should_DispatchException()
        {
            //arrange
            var target = GetSafeFiber();
            Exception error = null;
            //act
            try
            {
                await target.Enqueue(Throw);
            }
            catch (Exception e)
            {
                error = e;
            }

            //assert
            error.Should().NotBeNull();
        }

        [Fact]
        public async Task Enqueue_Task_Exception_Should_Not_Kill_MainThead()
        {
            //arrange
            var target = GetSafeFiber();
            //act
            try
            {
                await target.Enqueue(Throw);
            }
            catch
            {
            }

            await target.Enqueue(() => TaskFactory());
        }

        [Theory, AutoData]
        public async Task Enqueue_Task_T_Return_Result(int data)
        {
            //arrange
            var target = GetSafeFiber();

            //act
            var res = await target.Enqueue(() => TaskFactory<int>(data));

            //assert
            res.Should().Be(data);
        }

        [Fact]
        public async Task Enqueue_Task_T_Run_On_Separated_Thread()
        {
            var current = Thread.CurrentThread;
            //arrange
            var target = GetSafeFiber();

            //act
            await target.Enqueue(() => TaskFactory<int>(12));

            var thread = RunningThread;
            thread.Should().NotBeNull();
            thread.Should().NotBe(current);

            //assert
            await target.Enqueue(() => TaskFactory<int>(12));
            RunningThread.Should().NotBeNull();
            RunningThread.Should().Be(thread);
        }

        [Theory, AutoData]
        public async Task Enqueue_Task_T_With_Cancellation_Return_Result(int data)
        {
            var current = Thread.CurrentThread;
            //arrange
            var target = GetSafeFiber();

            //act
            var res = await target.Enqueue(() => TaskFactory<int>(data), CancellationToken.None);

            //assert
            res.Should().Be(data);
            RunningThread.Should().NotBeNull();
            RunningThread.Should().NotBe(current);
        }

        [Fact]
        public async Task Enqueue_Task_T_With_Cancellation_Run_On_Separated_Thread()
        {
            var current = Thread.CurrentThread;
            //arrange
            var target = GetSafeFiber();

            //act
            await target.Enqueue(() => TaskFactory<int>(12));

            var thread = RunningThread;
            thread.Should().NotBeNull();
            thread.Should().NotBe(current);

            //assert
            await target.Enqueue(() => TaskFactory<int>(12), CancellationToken.None);
            RunningThread.Should().NotBeNull();
            RunningThread.Should().Be(thread);
        }

        [Fact]
        public async Task Enqueue_Task_T_With_Cancellation_Immediately_Cancel_Tasks_Enqueued()
        {
            var target = GetSafeFiber();
            var tester = new TaskEnqueueWithCancellationTester(target);

            await tester.RunAndCancelTask_T();

            tester.TimeSpentToCancelTask.Should().BeLessThan(TimeSpan.FromSeconds(0.5));
        }

        [Fact]
        public async Task Enqueue_Task_T_With_Cancellation_Do_Not_Run_Task_Cancelled_Enqueued()
        {
            var target = GetSafeFiber();
            var tester = new TaskEnqueueWithCancellationTester(target);

            await tester.RunAndCancelTask_T();

            tester.CancelledTaskHasBeenExecuted.Should().BeFalse();
        }

        [Theory, AutoData]
        public async Task Enqueue_Task_T_With_Cancellation_Do_Not_Cancel_Running_Task(int value)
        {
            var target = GetSafeFiber();
            var tester = new TaskEnqueueWithCancellationTester(target);

            var result = await tester.CancelNotCancellableRunningTask_T(value);

            result.Should().Be(value);
        }

        [Fact]
        public async Task Enqueue_Task_T_With_Cancellation_Interpret_CanceledOperationException_As_Cancelled_Task()
        {
            var target = GetSafeFiber();
            var tester = new TaskEnqueueWithCancellationTester(target);

            var cancelledTask = tester.CancelCancellableRunningTask_T();
            var exception = await TaskEnqueueWithCancellationTester.AwaitForException(cancelledTask);

            exception.Should().NotBeNull();
            exception.Should().BeAssignableTo<TaskCanceledException>();
            cancelledTask.Status.Should().Be(TaskStatus.Canceled);
        }

        [Theory, AutoData]
        public async Task Enqueue_Func_T_Should_Work_AsExpected_With_Result(int value)
        {

            Thread current = Thread.CurrentThread;
            //arrange
            var target = GetSafeFiber();

            int Func()
            {
                RunningThread = Thread.CurrentThread;
                return value;
            }

            //act
           await target.Enqueue((Func<int>) Func);

            //assert
            value.Should().Be(value);
            RunningThread.Should().NotBeNull();
            RunningThread.Should().NotBe(current);
        }

        [Theory, AutoData]
        public async Task Enqueue_Func_T_With_Cancellation_Should_Work_AsExpected_With_Result(int value)
        {

            Thread current = Thread.CurrentThread;
            //arrange
            var target = GetSafeFiber();

            int Func()
            {
                RunningThread = Thread.CurrentThread;
                return value;
            }

            //act
            await target.Enqueue((Func<int>)Func, CancellationToken.None);

            //assert
            value.Should().Be(value);
            RunningThread.Should().NotBeNull();
            RunningThread.Should().NotBe(current);
        }

        [Fact]
        public async Task Enqueue_Task_T_Should_DispatchException_With_Result()
        {
            //arrange
            var target = GetSafeFiber();
            Exception error = null;
            //act
            try
            {
                await target.Enqueue(Throw<string>);
            }
            catch (Exception e)
            {
                error = e;
            }

            //assert
            error.Should().NotBeNull();
        }

        [Fact]
        public async Task Enqueue_Task_T_Exception_Should_Not_Kill_MainThead_With_Result()
        {
            //arrange
            var target = GetSafeFiber();
            //act
            try
            {
                await target.Enqueue(Throw<int>);
            }
            catch
            {
            }

            var res = await target.Enqueue(() => TaskFactory<int>(25));

            //assert
            res.Should().Be(25);
        }

        [Fact]
        public async Task Enqueue_Action_Should_Work_OnAction()
        {
            //arrange
            var target = GetSafeFiber();

            bool done = false;
            Action act = () => done = true;
            //act

            await target.Enqueue(act);

            //assert
            done.Should().BeTrue();
        }

        [Fact]
        public async Task Enqueue_Action_With_Cancellation_Should_Work_OnAction()
        {
            //arrange
            var target = GetSafeFiber();

            var done = false;
            void Act() => done = true;
            //act

            await target.Enqueue((Action) Act, CancellationToken.None);

            //assert
            done.Should().BeTrue();
        }

        [Fact]
        public async Task Enqueue_Action_With_Cancellation_Is_Cancellable()
        {
            //arrange
            var target = GetSafeFiber();

            var done = false;
            void Act() => done = true;
            //act

            Func<Task> @do = () => target.Enqueue((Action) Act, new CancellationToken(true));

            await @do.Should().ThrowAsync<OperationCanceledException>();

            //assert
            done.Should().BeFalse();
        }

        [Fact]
        public async Task Enqueue_Action_With_Cancellation_ReThrow_Exception()
        {
            //arrange
            var target = GetSafeFiber();

            void Act() => throw new ArgumentException();
            //act

            Func<Task> @do = () => target.Enqueue((Action)Act, CancellationToken.None);

            //assert
            await @do.Should().ThrowAsync<ArgumentException>();
        }

        [Fact]
        public async Task Enqueue_Action_Should_ReDispatch_Exception_OnAction()
        {
            //arrange
            var target = GetSafeFiber();
            Exception error = null;
            Action act = () => Throw();
            //act
            try
            {
                await target.Enqueue(act);
            }
            catch (Exception e)
            {
                error = e;
            }

            //assert
            error.Should().NotBeNull();
        }

        [Fact]
        public async Task Enqueue_Task_Should_Not_Cancel_Already_Running_Task_OnDispose()
        {
            Task newTask = null;

            //arrange
            var target = GetSafeFiber(t => t.Priority = ThreadPriority.Highest);
            newTask = target.Enqueue(() => TaskFactory(3));

            while (RunningThread == null)
            {
                Thread.Sleep(100);
            }

            await newTask;

            RunningThread.Should().NotBeNull();

            newTask.IsCanceled.Should().BeFalse();
        }

        [Fact]
        public async Task Enqueue_Task_Should_Cancel_Task_When_Added_On_Disposed_Queue()
        {
            //arrange
            var fiber = GetSafeFiber();
            var task = fiber.Enqueue(() => TaskFactory());
            await fiber.DisposeAsync();

            var newestTask = fiber.Enqueue(() => TaskFactory());

            TaskCanceledException error = null;
            try
            {
                await newestTask;
            }
            catch (TaskCanceledException e)
            {
                error = e;
            }

            newestTask.IsCanceled.Should().BeTrue();
            error.Should().NotBeNull();
        }

        [Fact]
        public async Task Enqueue_Action_Should_Cancel_Task_When_On_Disposed()
        {
            var fiber = GetSafeFiber();
            var task = fiber.Enqueue(() => TaskFactory());
            await fiber.DisposeAsync();

            var done = false;
            var newestTask = fiber.Enqueue(() => { done = true; });

            TaskCanceledException error = null;
            try
            {
                await newestTask;
            }
            catch (TaskCanceledException e)
            {
                error = e;
            }

            newestTask.IsCanceled.Should().BeTrue();
            error.Should().NotBeNull();
            done.Should().BeFalse();
        }

        [Fact]
        public async Task Enqueue_Func_T_Should_Cancel_Task_When_On_Disposed_Queue()
        {
            var fiber = GetSafeFiber();
            var task = fiber.Enqueue(() => TaskFactory());
            await fiber.DisposeAsync();

            Func<int> func = () => 25;
            Task newestTask = fiber.Enqueue(func);

            TaskCanceledException error = null;
            try
            {
                await newestTask;
            }
            catch (TaskCanceledException e)
            {
                error = e;
            }

            newestTask.IsCanceled.Should().BeTrue();
            error.Should().NotBeNull();
        }

        [Fact]
        public async Task Dispose_Running_Task_Should_Continue_After_Stopping_Queue()
        {
            //arrange  
            var fiber = GetSafeFiber();
            var task = fiber.Enqueue(() => TaskFactory(3));
            while (RunningThread == null)
            {
                Thread.Sleep(100);
            }

            //act
            await fiber.DisposeAsync();
            await task;

            //assert
            task.IsCompleted.Should().BeTrue();
        }

        [Fact]
        public async Task Dispose_Enqueue_Items_Should_Return_Canceled_Task_After_Stopping_Queue()
        {
            //arrange  
            var fiber = GetSafeFiber();
            await fiber.DisposeAsync();

            var done = false;

            TaskCanceledException error = null;
            try
            {
                await fiber.Enqueue(() => { done = true; });
            }
            catch (TaskCanceledException e)
            {
                error = e;
            }

            error.Should().NotBeNull();
            done.Should().BeFalse();
        }

        [Fact]
        public async Task Enqueue_Task_Should_Cancel_Not_Started_Task_When_OnDispose()
        {
            Task newTask = null, notStarted = null;

            //arrange
            var target = GetSafeFiber(t => t.Priority = ThreadPriority.Highest);
            newTask = target.Enqueue(() => TaskFactory(3));

            while (RunningThread == null)
            {
                Thread.Sleep(100);
            }
            await target.DisposeAsync();

            //act
            notStarted = target.Enqueue(() => TaskFactory(3));

            await newTask;

            //assert
            TaskCanceledException error = null;
            try
            {
                await notStarted;
            }
            catch (TaskCanceledException e)
            {
                error = e;
            }

            notStarted.IsCanceled.Should().BeTrue();
            error.Should().NotBeNull();
        }


        [Fact]
        public async Task Enqueue_Action_Should_Cancel_Not_Started_Task_When_OnDispose()
        {
            Task newTask = null, notStarted = null;
            var done = false;

            //arrange
            var target = GetSafeFiber(t => t.Priority = ThreadPriority.Highest);
            newTask = target.Enqueue(() => TaskFactory(3));

            while (RunningThread == null)
            {
                Thread.Sleep(100);
            }
            await target.DisposeAsync();

            notStarted = target.Enqueue(() => { done = true; });

            await newTask;

            TaskCanceledException error = null;
            try
            {
                await notStarted;
            }
            catch (TaskCanceledException e)
            {
                error = e;
            }

            notStarted.IsCanceled.Should().BeTrue();
            error.Should().NotBeNull();
            done.Should().BeFalse();
        }

        [Fact]
        public async Task Enqueue_Action_Runs_Actions_Sequencially()
        {
            var target = GetSafeFiber();
            var tester = new SequenceTester(target, _TestOutputHelper);
            await tester.Stress();
            tester.Count.Should().Be(tester.MaxThreads);
        }

        [Fact]
        public async Task Enqueue_Task_Runs_Actions_Sequencially_after_await()
        {
            var target = GetSafeFiber();
            var tester = new SequenceTester(target, _TestOutputHelper);
            await tester.StressTask();
            tester.Count.Should().Be(tester.MaxThreads);
        }

        public static IEnumerable<object[]> GetQueues() 
        {
            yield return new object[] { new SpinningMpscQueue<IWorkItem>() };
            yield return new object[] { new StandardMpscQueue<IWorkItem>() };
            yield return new object[] { new BlockingMpscQueue<IWorkItem>() };
        }

        private PerfTimer GetPerfTimer(int operation) => new PerfTimer(operation, _TestOutputHelper);

        [Theory]
        [MemberData(nameof(GetQueues))]
        public async Task Test_Queue_Performance(IMpScQueue<IWorkItem> queueWorkItem) 
        {
            _TestOutputHelper.WriteLine(queueWorkItem.GetType().Name);

            var fiber = GetSafeFiber(queueWorkItem);
            var reset = new AutoResetEvent(false);
            const int max = 5000000;

            Action<int> @do = (count) =>
            {
                if (count == max)
                {
                    reset.Set();
                }
            };

            using (GetPerfTimer(max))
            {
                for (var i = 0; i <= max; i++)
                {
                    var i1 = i;
                    fiber.Dispatch(() => @do(i1));
                }
                reset.WaitOne(30000, false).Should().BeTrue();
            }

            await fiber.DisposeAsync();
        }
    }
}
