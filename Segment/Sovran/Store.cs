using System;
using System.Collections.Generic;
using System.Linq;
using Segment.Concurrent;

namespace Segment.Sovran
{
    public class Store
    {
        internal List<Container> States { get; set; }

        internal List<Subscription> Subscribers { get; set; }

        private Scope _scope;

        private Dispatcher _syncQueue;

        private Dispatcher _updateQueue;

        public Store()
        {
            States = new List<Container>();
            Subscribers = new List<Subscription>();

            _scope = new Scope();
            _syncQueue = new Dispatcher(new LimitedConcurrencyLevelTaskScheduler(1));
            _updateQueue = new Dispatcher(new LimitedConcurrencyLevelTaskScheduler(1));
        }

        public int Subscribe<TState>(ISubscriber subscriber, Action<IState> handler, bool initialState = false) where TState : IState
        { 
            var subscription = new Subscription(subscriber, handler, typeof(TState));

            _scope.Async(_syncQueue, _ =>
            {
                Subscribers.Add(subscription);
                return;
            });

            if (initialState)
            {
                var state = CurrentState<TState>();
                if (state != null)
                {
                    Notify(new List<Subscription> { subscription }, state);
                }
            }
            return subscription.SubscriptionID;
        }

        public void Unsubscribe(int identifier)
        {
            Subscribers.RemoveAll(item => item.SubscriptionID == identifier);
        }

        public void Provide<TState>(TState state) where TState : IState
        {
            var exists = Existing(state);
            if (exists.Count != 0)
            {
                // Ignore it since it is already there
                return;
            }

            var container = new Container(state);
            States.Add(container);

            // Get any handlers that may have been added prior to state
            // being provided that work against TState.
            var subscriptions = ExistingSubscribers<TState>();
            Notify(subscriptions, state);
        }

        /**
         * Dispatch
         */
        public void Dispatch<TAction, TState>(TAction action) where TAction : IAction where TState : IState
        {
            var existingStates = ExistingStatesOfTStateype<TState>();
            if (existingStates.Count == 0)
            {
                return;
            }

            var targetContainer = existingStates.FirstOrDefault();
            if (targetContainer != null)
            {
                var state = targetContainer.State;
                state = action.Reduce(state);
                targetContainer.State = state;

                var subs = ExistingSubscribers<TState>();
                Notify(subs, state);
            }
        }


        public TState CurrentState<TState>() where TState : IState
        {
            var container = ExistingStatesOfTStateype<TState>().First();
            if (container.State is TState state)
            {
                return state;
            }

            return default;
        }

        /**
         * State Lookup
         */
        private List<Container> Existing<T>(T state) where T : IState
        {
            var result = States.FindAll(o => o.State.GetType() == state.GetType());
            return result;
        }

        private List<Container> ExistingStatesOfTStateype<T>() where T : IState
        {
            var result = States.FindAll(o => o.State.GetType() == typeof(T));
            return result;
        }

        /**
         * Subscriber lookup
         */
        private List<Subscription> ExistingSubscribers<T>() where T : IState
        {
            return Subscribers.FindAll(item => item.HandlerType == typeof(T));
        }

        private void Notify<T>(List<Subscription> subscribers, T state) where T : IState
        {
            // TODO: Turn this into an asynchronous task   
            foreach (var subscription in subscribers)
            {
                subscription.Handler?.Invoke(state);
            }

            // Clean up any expired subscribers.
            Clean();
        }

        public void Clean()
        {
            Subscribers.RemoveAll(sub => sub.Owner == null);
        }
    }

    ////
    // Internal classes
    internal class Subscription
    {
        internal ISubscriber Owner { get; }
        internal Action<IState> Handler { get; }
        internal Type HandlerType { get; }
        internal int SubscriptionID { get; }

        internal Subscription(ISubscriber owner, Action<IState> handler, Type handlerType)
        {
            this.Owner = owner;
            this.Handler = handler;
            this.HandlerType = handlerType;
            this.SubscriptionID = CreateNextSubscriptionID();
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
