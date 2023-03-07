using System.Collections.Generic;
using Segment.Sovran;

namespace Tests
{

    public class Message
    {
        internal string from;
        internal string to;
        internal string content;
        internal List<string> photos;
    }

    public class MessageState : IState
    {
        internal int unreadCount = 0;
        internal int outgoingCount = 0;
        internal List<Message> messages = new List<Message>();
        internal List<Message> outgoing = new List<Message>();
    }

    public class UserState : IState
    {
        internal string username = null;
        internal string token = null;
    }

    public class NotProvidedState : IState
    {
        internal int value = 0;
    }

    public class MessagesUnreadAction : IAction
    {
        internal int value;

        public IState Reduce(IState state)
        {
            if (state is MessageState newState)
            {
                newState.unreadCount = value;
                return newState;
            }

            return default;
        }
    }

    public class MyResultType
    {
        internal int value;
    }

    public class NotProvidedAction : IAction
    {
        public IState Reduce(IState state)
        {
            return state;
        }
    }
}
