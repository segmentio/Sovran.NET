using System.Linq;
using System.Threading.Tasks;
using Segment.Sovran;
using Xunit;

namespace Tests
{

    public class SovranTest : ISubscriber
    {
        private Store store = new Store();

        [Fact]
        public async Task TestProvide()
        {
            await store.Provide(new MessageState());
            Assert.Equal(1, store.States.Count);

            await store.Provide(new UserState());
            Assert.Equal(2, store.States.Count);
        }


        [Fact]
        public async Task TestDoubleSubscribe()
        {
            await store.Provide(new MessageState());
            await store.Provide(new UserState());

            var id1 = await store.Subscribe<MessageState>(this, _ => { });

            await store.Subscribe<UserState>(this, _ => { });

            var id3 = await store.Subscribe<UserState>(this, _ => { });

            Assert.Equal(3, store.Subscribers.Count);
            Assert.Equal(id1 + 2, id3);
        }

        [Fact]
        public async Task TestDoubleProvide()
        {
            await store.Provide(new MessageState());
            await store.Provide(new UserState());

            // this should do nothing since UserState has already been provided.
            // in use, this will assert in DEBUG mode, outside of tests.
            await store.Provide(new UserState());

            Assert.Equal(2, store.States.Count);
        }

        [Fact]
        public async Task TestDispatch()
        {
            await store.Provide(new MessageState());
            await store.Subscribe<MessageState>(this, _ => { });

            var expected = new MessagesUnreadAction
            {
                value = 22
            };
            await store.Dispatch<MessagesUnreadAction, MessageState>(expected);

            var actual = await store.CurrentState<MessageState>();

            Assert.Equal(expected.value, actual.unreadCount);
        }

        [Fact]
        public async Task TestUnprovidedStateDispatch()
        {
            var called = false;
            await store.Provide(new MessageState());
            await store.Subscribe<NotProvidedState>(this, _ =>
            {
                // we should never get here because NotProvidedState isn't what's in the store.
                called = true;
            });

            var action = new NotProvidedAction();
            // this action should get dropped, because there's no matching state for it.
            await store.Dispatch<NotProvidedAction, NotProvidedState>(action);

            Assert.False(called);
        }

        [Fact]
        public async Task TestUnsubscribeForAction()
        {
            var called = 0;
            await store.Provide(new MessageState());
            var identifier = await store.Subscribe<MessageState>(this, state =>
            {
                called++;
                var messageState = state as MessageState;
                Assert.Equal(22, messageState?.unreadCount);
            });

            var action = new MessagesUnreadAction
            {
                value = 22
            };
            await store.Dispatch<MessagesUnreadAction, MessageState>(action);

            var subscriptionCount = store.Subscribers.Count;
            await store.Unsubscribe(identifier);

            Assert.False(store.Subscribers.Any(o => o.SubscriptionID == identifier));
            Assert.Equal(subscriptionCount - 1, store.Subscribers.Count);

            var nextAction = new MessagesUnreadAction
            {
                value = 11
            };
            // this should be ignored since we've unsubscribed.
            await store.Dispatch<MessagesUnreadAction, MessageState>(nextAction);

            Assert.Equal(1, called);
        }

        [Fact]
        public void TestSubscriptionIdIncrement()
        {
            var s1 = new Subscription(this, _ => { }, typeof(MessageState), default);
            var s2 = new Subscription(this, _ => { }, typeof(MessageState), default);
            var s3 = new Subscription(this, _ => { }, typeof(MessageState), default);

            Assert.True(s1.SubscriptionID + 1 == s2.SubscriptionID);
            Assert.True(s2.SubscriptionID + 1 == s3.SubscriptionID);
        }

        [Fact]
        public async Task TestGetCurrentState()
        {
            var expected = new MessageState
            {
                unreadCount = 1,
                outgoingCount = 2
            };

            await store.Provide(expected);
            var actual = await store.CurrentState<MessageState>();

            Assert.Equal(expected.unreadCount, actual.unreadCount);
            Assert.Equal(expected.outgoingCount, actual.outgoingCount);
        }
    }
}