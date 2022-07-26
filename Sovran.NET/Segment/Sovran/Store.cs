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

        private readonly Dispatcher _syncQueue;

        private readonly Dispatcher _updateQueue;

        private readonly Dispatcher _defaultQueue;

        public Store()
        {
            States = new List<Container>();
            Subscribers = new List<Subscription>();

            _scope = new Scope();
            _syncQueue = new Dispatcher(new LimitedConcurrencyLevelTaskScheduler(1));
            _updateQueue = new Dispatcher(new LimitedConcurrencyLevelTaskScheduler(1));
            _defaultQueue = new Dispatcher(new LimitedConcurrencyLevelTaskScheduler(Environment.ProcessorCount));
        }

        public async Task<int> Subscribe<TState>(ISubscriber subscriber, Action<IState> handler, bool initialState = false, Dispatcher queue = default) where TState : IState
        {
            if (queue == null)
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
                var state = await CurrentState<TState>();
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
            var exists = await Existing(state);
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
            var subscriptions = await ExistingSubscribers<TState>();
            await Notify(subscriptions, state);
        }

        /**
         * Dispatch
         */
        public async Task Dispatch<TAction, TState>(TAction action) where TAction : IAction where TState : IState
        {
            var existingStates = await ExistingStatesOfTStateype<TState>();
            if (existingStates.Count == 0)
            {
                return;
            }

            var targetContainer = existingStates.FirstOrDefault();
            if (targetContainer != null)
            {
                var state = targetContainer.State;

                await _scope.Launch(_updateQueue, delegate
                {
                    state = action.Reduce(state);
                    targetContainer.State = state;
                });

                var subs = await ExistingSubscribers<TState>();
                await Notify(subs, state);
            }
        }


        public async Task<TState> CurrentState<TState>() where TState : IState
        {
            var matchingStates = await ExistingStatesOfTStateype<TState>();

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
            var result = await _scope.Async(_updateQueue, delegate
            {
                return States.FindAll(o => o.State.GetType() == state.GetType()); ;
            });

            return result;
        }

        private async Task<List<Container>> ExistingStatesOfTStateype<T>() where T : IState
        {
            var result = await _scope.Async(_updateQueue, delegate
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
            var subscribers = await _scope.Async(_syncQueue, delegate
            {
                return Subscribers.FindAll(item => item.HandlerType == typeof(T));
            });

            return subscribers;
        }

        private async Task Notify<T>(List<Subscription> subscribers, T state) where T : IState
        {
            foreach (var subscription in subscribers)
            {
                var handler = subscription.Handler;
                var ownerAlive = subscription.Owner.TryGetTarget(out var _);
                if (handler == null || !ownerAlive) continue;


                var _ = _scope.Launch(subscription.Queue, delegate
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
        internal Dispatcher Queue { get; }

        internal Subscription(ISubscriber owner, Action<IState> handler,
            Type handlerType, Dispatcher queue)
        {
            this.Owner = new WeakReference<ISubscriber>(owner);
            this.Handler = handler;
            this.HandlerType = handlerType;
            this.SubscriptionID = CreateNextSubscriptionID();
            this.Queue = queue;
        }

        private static int _nextSubscriptionID = 1;
        private static int CreateNextSubscriptionID()
        {
            var result = _nextSubscriptionID;
            _nextSubscriptionID++;
            return result;
        }
    }

    internal class Container
    {
        internal IState State { get; set; }

        internal Container(IState state)
        {
            this.State = state;
        }
    }

}
