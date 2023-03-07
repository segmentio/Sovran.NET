using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Segment.Concurrent;

namespace Segment.Sovran
{
    public class Store
    {
        internal List<Container> States { get; set; }

        internal List<Subscription> Subscribers { get; set; }

        private readonly Scope _scope;

        private readonly IDispatcher _syncQueue;

        private readonly IDispatcher _updateQueue;

        private readonly IDispatcher _defaultQueue;

        private readonly bool _useSynchronizeDispatcher;

        public Store(bool useSynchronizeDispatcher = false, ICoroutineExceptionHandler exceptionHandler = default)
        {
            States = new List<Container>();
            Subscribers = new List<Subscription>();

            _scope = new Scope(exceptionHandler);
            _useSynchronizeDispatcher = useSynchronizeDispatcher;
            if (useSynchronizeDispatcher)
            {
                _defaultQueue = new SynchronizeDispatcher();
                _syncQueue = _defaultQueue;
                _updateQueue = _defaultQueue;
            }
            else
            {
                _syncQueue = new Dispatcher(new LimitedConcurrencyLevelTaskScheduler(1));
                _updateQueue = new Dispatcher(new LimitedConcurrencyLevelTaskScheduler(1));
                _defaultQueue = new Dispatcher(new LimitedConcurrencyLevelTaskScheduler(Environment.ProcessorCount));
            }
        }

        public async Task<int> Subscribe<TState>(ISubscriber subscriber, Action<IState> handler, bool initialState = false, IDispatcher queue = default) where TState : IState
        {
            if (queue == null || _useSynchronizeDispatcher)
            {
                queue = _defaultQueue;
            }
            var subscription = new Subscription(subscriber, handler, typeof(TState), queue);

            await _scope.Launch(_syncQueue, delegate
            {
                Subscribers.Add(subscription);
            });

            if (initialState)
            {
                TState state = await CurrentState<TState>();
                if (state != null)
                {
                    await Notify(new List<Subscription> { subscription }, state);
                }
            }
            return subscription.SubscriptionID;
        }

        public async Task Unsubscribe(int identifier)
        {
            await _scope.Launch(_syncQueue, delegate
            {
                Subscribers.RemoveAll(item => item.SubscriptionID == identifier);
            });
        }

        public async Task Provide<TState>(TState state) where TState : IState
        {
            List<Container> exists = await Existing(state);
            if (exists.Count != 0)
            {
                // Ignore it since it is already there
                return;
            }

            var container = new Container(state);

            await _scope.Launch(_updateQueue, delegate
            {
                States.Add(container);
            });

            // Get any handlers that may have been added prior to state
            // being provided that work against TState.
            List<Subscription> subscriptions = await ExistingSubscribers<TState>();
            await Notify(subscriptions, state);
        }

        /**
         * Dispatch
         */
        public async Task Dispatch<TAction, TState>(TAction action) where TAction : IAction where TState : IState
        {
            List<Container> existingStates = await ExistingStatesOfType<TState>();
            if (existingStates.Count == 0)
            {
                return;
            }

            Container targetContainer = existingStates.FirstOrDefault();
            if (targetContainer != null)
            {
                IState state = targetContainer.State;

                await _scope.Launch(_updateQueue, delegate
                {
                    state = action.Reduce(state);
                    targetContainer.State = state;
                });

                List<Subscription> subs = await ExistingSubscribers<TState>();
                await Notify(subs, state);
            }
        }


        public async Task<TState> CurrentState<TState>() where TState : IState
        {
            List<Container> matchingStates = await ExistingStatesOfType<TState>();

            if (matchingStates.Count <= 0) return default;
            if (matchingStates[0].State is TState state)
            {
                return state;
            }
            return default;
        }

        /**
         * State Lookup
         */
        private async Task<List<Container>> Existing<T>(T state) where T : IState
        {
            List<Container> result = await _scope.Async(_updateQueue, delegate
            {
                return States.FindAll(o => o.State.GetType() == state.GetType()); ;
            });

            return result;
        }

        private async Task<List<Container>> ExistingStatesOfType<T>() where T : IState
        {
            List<Container> result = await _scope.Async(_updateQueue, delegate
            {
                return States.FindAll(o => o.State.GetType() == typeof(T));
            });

            return result;
        }

        /**
         * Subscriber lookup
         */
        private async Task<List<Subscription>> ExistingSubscribers<T>() where T : IState
        {
            List<Subscription> subscribers = await _scope.Async(_syncQueue, delegate
            {
                return Subscribers.FindAll(item => item.HandlerType == typeof(T));
            });

            return subscribers;
        }

        private async Task Notify<T>(List<Subscription> subscribers, T state) where T : IState
        {
            foreach (Subscription subscription in subscribers)
            {
                Action<IState> handler = subscription.Handler;
                bool ownerAlive = subscription.Owner.TryGetTarget(out ISubscriber _);
                if (handler == null || !ownerAlive) continue;


                Task _ = _scope.Launch(subscription.Queue, delegate
                {
                    handler(state);
                });
            }

            // Clean up any expired subscribers.
            await Clean();
        }

        public async Task Clean()
        {
            await _scope.Launch(_syncQueue, delegate
            {
                Subscribers.RemoveAll(sub => sub.Owner == null);
            });
        }
    }

    ////
    // Internal classes
    internal class Subscription
    {
        internal WeakReference<ISubscriber> Owner { get; }
        internal Action<IState> Handler { get; }
        internal Type HandlerType { get; }
        internal int SubscriptionID { get; }
        internal IDispatcher Queue { get; }

        internal Subscription(ISubscriber owner, Action<IState> handler,
            Type handlerType, IDispatcher queue)
        {
            Owner = new WeakReference<ISubscriber>(owner);
            Handler = handler;
            HandlerType = handlerType;
            SubscriptionID = CreateNextSubscriptionID();
            Queue = queue;
        }

        private static int _nextSubscriptionID = 1;
        private static int CreateNextSubscriptionID()
        {
            int result = _nextSubscriptionID;
            _nextSubscriptionID++;
            return result;
        }
    }

    internal class Container
    {
        internal IState State { get; set; }

        internal Container(IState state)
        {
            State = state;
        }
    }

}
