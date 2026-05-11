using System.Threading;

namespace Unosquare.FFME.Windows.Sample.Foundation
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Runtime.CompilerServices;

    /// <summary>
    /// A very simple set of extensions to more easily handle UI state changes based on
    /// notification properties. The main idea is to bind to the PropertyChanged event
    /// for a publisher only one and add a set of callbacks with matching property names
    /// when the publisher raises the event.
    /// </summary>
    internal static class ReactiveExtensions
    {
        /// <summary>
        /// Contains a list of Subscriptions[Publisher][PropertyName].List of subscriber-action pairs.
        /// </summary>
        private static readonly ConditionalWeakTable<INotifyPropertyChanged, SubscriptionSet> Subscriptions = [];

        private static readonly Lock SyncLock = new();

        /// <summary>
        /// Specifies a callback when properties change.
        /// </summary>
        /// <param name="publisher">The publisher.</param>
        /// <param name="callback">The callback.</param>
        /// <param name="propertyNames">The property names.</param>
        public static void WhenChanged(this INotifyPropertyChanged publisher, Action callback, params string[] propertyNames)
        {
            ArgumentNullException.ThrowIfNull(publisher);
            ArgumentNullException.ThrowIfNull(callback);
            ArgumentNullException.ThrowIfNull(propertyNames);

            var bindPropertyChanged = false;
            
            lock (SyncLock)
            {
                if (!Subscriptions.TryGetValue(publisher, out SubscriptionSet subscriptionSet))
                {
                    subscriptionSet = [];
                    Subscriptions.Add(publisher, subscriptionSet);
                    bindPropertyChanged = true;
                }

                foreach (var propertyName in propertyNames)
                {
                    if (subscriptionSet != null)
                    {
                        if (!subscriptionSet.ContainsKey(propertyName))
                            subscriptionSet[propertyName] = [];

                        subscriptionSet[propertyName].Add(callback);
                    }
                }
            }

            // Make an initial call
            callback();

            if (bindPropertyChanged)
                publisher.PropertyChanged += OnPublisherPropertyChanged;
        }

        private static void OnPublisherPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (sender is not INotifyPropertyChanged publisher)
                return;

            CallbackList propertyCallbacks = null;

            lock (SyncLock)
            {
                if (!Subscriptions.TryGetValue(publisher, out var subscriptionSet))
                    return;

                if (!subscriptionSet.TryGetValue(e.PropertyName, out var callbacks))
                    return;

                propertyCallbacks = [.. callbacks];
            }

            foreach (var propertyCallback in propertyCallbacks)
                propertyCallback();
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
