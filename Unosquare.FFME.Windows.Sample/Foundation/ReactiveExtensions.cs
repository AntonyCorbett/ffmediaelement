using System.Threading;

namespace Unosquare.FFME.Windows.Sample.Foundation
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;

    /// <summary>
    /// A very simple set of extensions to more easily handle UI state changes based on
    /// notification properties. The main idea is to bind to the PropertyChanged event
    /// for a publisher only one and add a set of callbacks with matching property names
    /// when the publisher raises the event.
    /// </summary>
    internal static class ReactiveExtensions
    {
        // todo: this implementation is not memory efficient as it does not remove dead references to the publisher or the callbacks.
        ///
        /// 1.	Publishers are never removed from the dictionary - Once a publisher is added to Subscriptions, it stays there indefinitely, preventing garbage collection even after the publisher is no longer needed.
        /// 2.	Event handler keeps references alive - The PropertyChanged event handler (registered around line 62) holds a reference to publisher, keeping it in memory as long as the subscription exists.
        /// 3.	No cleanup mechanism - There's no way to unsubscribe or clean up when a publisher is disposed.

        /// <summary>
        /// Contains a list of subscriptions Subscriptions[Publisher][PropertyName].List of subscriber-action pairs.
        /// </summary>
        private static readonly Dictionary<INotifyPropertyChanged, SubscriptionSet> Subscriptions = [];

        private static readonly Lock SyncLock = new();

        /// <summary>
        /// Specifies a callback when properties change.
        /// </summary>
        /// <param name="publisher">The publisher.</param>
        /// <param name="callback">The callback.</param>
        /// <param name="propertyNames">The property names.</param>
        public static void WhenChanged(this INotifyPropertyChanged publisher, Action callback, params string[] propertyNames)
        {
            var bindPropertyChanged = false;

            lock (SyncLock)
            {
                // Create the subscription set for the publisher if it does not exist.
                if (Subscriptions.ContainsKey(publisher) == false)
                {
                    Subscriptions[publisher] = [];

                    // if it did not exist before, we need to bind to the
                    // PropertyChanged event of the publisher.
                    bindPropertyChanged = true;
                }

                foreach (var propertyName in propertyNames)
                {
                    // Create the set of callback references for the publisher's property if it does not exist.
                    if (Subscriptions[publisher].ContainsKey(propertyName) == false)
                        Subscriptions[publisher][propertyName] = new CallbackList();

                    // Add the callback for the publisher's property changed
                    Subscriptions[publisher][propertyName].Add(callback);
                }
            }

            // Make an initial call
            callback();

            // No need to bind to the PropertyChanged event if we are already bound to it.
            if (bindPropertyChanged == false)
                return;

            // Finally, bind to property changed
            publisher.PropertyChanged += (s, e) =>
            {
                CallbackList propertyCallbacks = null;

                lock (SyncLock)
                {
                    // we don't need to perform any action if there are no subscriptions to
                    // this property name.
                    if (Subscriptions[publisher].ContainsKey(e.PropertyName) == false)
                        return;

                    // Get the list of alive subscriptions for this property name
                    propertyCallbacks = Subscriptions[publisher][e.PropertyName];
                }

                // Call the subscription's callbacks
                foreach (var propertyCallback in propertyCallbacks)
                {
                    // if the subscription is alive, invoke the matching action
                    propertyCallback.Invoke();
                }
            };
        }

        internal sealed class SubscriptionSet : Dictionary<string, CallbackList> { }

        internal sealed class CallbackList : List<Action>
        {
            public CallbackList()
                : base(32)
            {
                // placeholder
            }
        }
    }
}
