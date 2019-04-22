﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Unity.Builder;
using Unity.Extensions;
using Unity.Policy;
using Unity.Registration;
using Unity.Resolution;
using Unity.Storage;

namespace Unity
{
    public partial class UnityContainer
    {
        #region Check if registered

        internal bool IsRegistered(Type type)
        {
            var hashCode = type?.GetHashCode() ?? 0 & UnityContainer.HashMask;

            // Iterate through containers hierarchy
            for (var container = this; null != container; container = container._parent)
            {
                // Skip to parent if no registrations
                if (null == container._metadata) continue;

                if (container._metadata.Contains(hashCode, type)) return true;
            }

            return false;
        }

        internal bool IsRegistered(ref BuilderContext context)
        {
            Type generic = null;
            int targetBucket, hashGeneric = -1, hashDefault = -1;
            int hashExact = NamedType.GetHashCode(context.Type, context.Name) & HashMask;

#if NETSTANDARD1_0 || NETCOREAPP1_0
            var info = context.Type.GetTypeInfo();
            if (info.IsGenericType)
            {
                generic = info.GetGenericTypeDefinition();
                hashDefault = NamedType.GetHashCode(generic, null) & HashMask;
                hashGeneric = (null != context.Name) ? NamedType.GetHashCode(generic, context.Name) & HashMask : hashDefault;
            }
#else
            if (context.Type.IsGenericType)
            {
                generic = context.Type.GetGenericTypeDefinition();
                hashDefault = NamedType.GetHashCode(generic, null) & HashMask;
                hashGeneric = (null != context.Name) ? NamedType.GetHashCode(generic, context.Name) & HashMask : hashDefault;
            }
#endif

            // Iterate through containers hierarchy
            for (var container = this; null != container; container = container._parent)
            {
                // Skip to parent if no registrations
                if (null == container._metadata) continue;

                var registry = container._registry;

                // Check for exact match
                targetBucket = hashExact % registry.Buckets.Length;
                for (var i = registry.Buckets[targetBucket]; i >= 0; i = registry.Entries[i].Next)
                {
                    ref var candidate = ref registry.Entries[i];
                    if (candidate.HashCode != hashExact || candidate.Key.Type != context.Type) continue;

                    // Found a registration
                    return true;
                }

                // Skip to parent if not generic
                if (null == generic) continue;

                // Check for factory with same name
                targetBucket = hashGeneric % registry.Buckets.Length;
                for (var i = registry.Buckets[targetBucket]; i >= 0; i = registry.Entries[i].Next)
                {
                    ref var candidate = ref registry.Entries[i];
                    if (candidate.HashCode != hashGeneric || candidate.Key.Type != generic) continue;

                    // Found a factory
                    return true;
                }

                // Skip to parent if not generic
                if (hashGeneric == hashDefault) continue;

                // Check for default factory
                targetBucket = hashDefault % registry.Buckets.Length;
                for (var i = registry.Buckets[targetBucket]; i >= 0; i = registry.Entries[i].Next)
                {
                    ref var candidate = ref registry.Entries[i];
                    if (candidate.HashCode != hashDefault || candidate.Key.Type != generic) continue;

                    // Found a factory
                    return true;
                }

            }

            return false;
        }

        #endregion


        #region Getting Registration During Resolution

        internal ImplicitRegistration GetRegistration(Type type, string name)
        {
#if NETSTANDARD1_0 || NETCOREAPP1_0
            var info = type.GetTypeInfo();
            if (info.IsGenericType) return GetGenericRegistration(type, name, info); 
#else
            if (type.IsGenericType) return GetGenericRegistration(type, name);
#endif
            int hashExact = NamedType.GetHashCode(type, name) & HashMask;

            // Iterate through containers hierarchy
            for (var container = this; null != container; container = container._parent)
            {
                // Skip to parent if no registrations
                if (null == container._metadata) continue;

                var registry = container._registry;

                // Check for exact match
                for (var i = registry.Buckets[hashExact % registry.Buckets.Length]; i >= 0; i = registry.Entries[i].Next)
                {
                    ref var candidate = ref registry.Entries[i];
                    if (candidate.HashCode != hashExact || candidate.Key.Type != type)
                        continue;

                    // Found a registration
                    if (!(candidate.Value is ImplicitRegistration registration))
                    {
                        registration = CreateRegistration(type, candidate.Value);
                        candidate.Value = registration;
                    }

                    return registration;
                }
            }

            return _root.GetOrAdd(hashExact, type, name, null);
        }

#if NETSTANDARD1_0 || NETCOREAPP1_0
        private ImplicitRegistration GetGenericRegistration(Type type, string name, TypeInfo info)
#else
        private ImplicitRegistration GetGenericRegistration(Type type, string name)
#endif
        {
            int targetBucket;
            int hashExact = NamedType.GetHashCode(type, name) & HashMask;

#if NETSTANDARD1_0 || NETCOREAPP1_0
            var generic = info.GetGenericTypeDefinition();
            var hashNull = NamedType.GetHashCode(generic, null) & HashMask;
            var hashGeneric = (null != name) ? NamedType.GetHashCode(generic, name) & HashMask : hashNull;
            var hashDefault = generic?.GetHashCode() ?? 0 & HashMask;
#else
            var generic = type.GetGenericTypeDefinition();
            var hashNull = NamedType.GetHashCode(generic, null) & HashMask;
            var hashGeneric = (null != name) ? NamedType.GetHashCode(generic, name) & HashMask : hashNull;
            var hashDefault = generic?.GetHashCode() ?? 0 & HashMask;
#endif

            // Iterate through containers hierarchy
            for (var container = this; null != container; container = container._parent)
            {
                // Skip to parent if no registrations
                if (null == container._metadata) continue;

                var registry = container._registry;

                // Check for exact match
                targetBucket = hashExact % registry.Buckets.Length;
                for (var i = registry.Buckets[targetBucket]; i >= 0; i = registry.Entries[i].Next)
                {
                    ref var candidate = ref registry.Entries[i];
                    if (candidate.HashCode != hashExact || candidate.Key.Type != type)
                        continue;

                    // Found a registration
                    if (!(candidate.Value is ImplicitRegistration registration))
                    {
                        registration = CreateRegistration(type, candidate.Value);
                        candidate.Value = registration;
                    }

                    return registration;
                }

                // Check for factory with same name
                targetBucket = hashGeneric % registry.Buckets.Length;
                for (var i = registry.Buckets[targetBucket]; i >= 0; i = registry.Entries[i].Next)
                {
                    ref var candidate = ref registry.Entries[i];
                    if (candidate.HashCode != hashGeneric || candidate.Key.Type != generic)
                        continue;

                    // Found a factory
                    return container.GetOrAdd(hashExact, type, name, candidate.Value);
                }

                // Skip if name is null, we just checked it above
                if (hashGeneric != hashNull)
                {
                    // Check for factory with 'null'
                    targetBucket = hashNull % registry.Buckets.Length;
                    for (var i = registry.Buckets[targetBucket]; i >= 0; i = registry.Entries[i].Next)
                    {
                        ref var candidate = ref registry.Entries[i];
                        if (candidate.HashCode != hashNull || candidate.Key.Type != generic)
                            continue;

                        // Found a factory
                        return container.GetOrAdd(hashExact, type, name, candidate.Value);
                    }
                }

                // Check for default factory
                targetBucket = hashDefault % registry.Buckets.Length;
                for (var i = registry.Buckets[targetBucket]; i >= 0; i = registry.Entries[i].Next)
                {
                    ref var candidate = ref registry.Entries[i];
                    if (candidate.HashCode != hashDefault || candidate.Key.Type != generic)
                        continue;

                    // Found a factory
                    return container.GetOrAdd(hashExact, type, name, candidate.Value);
                }
            }

            return _root.GetOrAdd(hashExact, type, name, null);
        }

        #endregion


        #region Registration manipulations

        private ImplicitRegistration InitAndAdd(Type type, string name, ImplicitRegistration registration)
        {
            lock (_syncRegistry)
            {
                if (null == _registry) _registry = new Registry<NamedType, IPolicySet>();
                if (null == _metadata)
                {
                    _metadata = new Registry<Type, int[]>();

                    Register = AddOrReplace;
                }
            }

            return Register(type, name, registration);
        }

        private ImplicitRegistration AddOrReplace(Type type, string name, ImplicitRegistration registration)
        {
            int position = -1;
            var collisions = 0;

            // Registry
            lock (_syncRegistry)
            {
                registration.AddRef();
                var hashCode = NamedType.GetHashCode(type, name) & HashMask;
                var targetBucket = hashCode % _registry.Buckets.Length;
                for (var i = _registry.Buckets[targetBucket]; i >= 0; i = _registry.Entries[i].Next)
                {
                    ref var candidate = ref _registry.Entries[i];
                    if (candidate.HashCode != hashCode || candidate.Key.Type != type)
                    {
                        collisions++;
                        continue;
                    }

                    // Swap the registration
                    var existing = candidate.Value as ImplicitRegistration;

                    if (null == existing) registration.Add(candidate.Value);
                    candidate.Value = registration;

                    // Replaced registration
                    return existing;
                }

                // Expand if required
                if (_registry.RequireToGrow || CollisionsCutPoint < collisions)
                {
                    _registry = new Registry<NamedType, IPolicySet>(_registry);
                    targetBucket = hashCode % _registry.Buckets.Length;
                }

                // Create new entry
                ref var entry = ref _registry.Entries[_registry.Count];
                entry.HashCode = hashCode;
                entry.Next = _registry.Buckets[targetBucket];
                entry.Key.Type = type;
                entry.Key.Name = name;
                entry.Value = registration;
                position = _registry.Count++;
                _registry.Buckets[targetBucket] = position;
            }

            collisions = 0;

            // Metadata
            lock (_syncMetadata)
            {
                var hashCode = type?.GetHashCode() ?? 0 & HashMask;
                var targetBucket = hashCode % _metadata.Buckets.Length;

                for (var i = _metadata.Buckets[targetBucket]; i >= 0; i = _metadata.Entries[i].Next)
                {
                    ref var candidate = ref _metadata.Entries[i];
                    if (candidate.HashCode != hashCode || candidate.Key != type)
                    {
                        collisions++;
                        continue;
                    }

                    // Expand if required
                    if (candidate.Value.Length == candidate.Value[0])
                    {
                        var source = candidate.Value;
                        candidate.Value = new int[source.Length * 2];
                        Array.Copy(source, candidate.Value, source[0]);
                    }

                    // Add to existing
                    candidate.Value[candidate.Value[0]++] = position;

                    // Nothing to replace
                    return null;
                }

                // Expand if required
                if (_metadata.RequireToGrow || CollisionsCutPoint < collisions)
                {
                    _metadata = new Registry<Type, int[]>(_metadata);
                    targetBucket = hashCode % _metadata.Buckets.Length;
                }

                // Create new metadata entry
                ref var entry = ref _metadata.Entries[_metadata.Count];
                entry.Next = _metadata.Buckets[targetBucket];
                entry.HashCode = hashCode;
                entry.Key = type;
                entry.Value = new int[] { 2, position };
                _metadata.Buckets[targetBucket] = _metadata.Count++;
            }

            // Nothing to replace
            return null;
        }

        private ImplicitRegistration GetOrAdd(int hashCode, Type type, string name, IPolicySet factory)
        {
            lock (_syncRegistry)
            {
                var collisions = 0;
                var targetBucket = hashCode % _registry.Buckets.Length;

                // Check for the existing 
                for (var i = _registry.Buckets[targetBucket]; i >= 0; i = _registry.Entries[i].Next)
                {
                    ref var candidate = ref _registry.Entries[i];
                    if (candidate.HashCode != hashCode || candidate.Key.Type != type)
                    {
                        collisions++;
                        continue;
                    }

                    if (!(candidate.Value is ImplicitRegistration value))
                    {
                        value = CreateRegistration(type, candidate.Value);
                        candidate.Value = value;
                    }

                    return value;
                }

                // Expand if required
                if (_registry.RequireToGrow || CollisionsCutPoint < collisions)
                {
                    _registry = new Registry<NamedType, IPolicySet>(_registry);
                    targetBucket = hashCode % _registry.Buckets.Length;
                }

                // Add registration
                var registration = CreateRegistration(type, factory);
                registration.AddRef();
                ref var entry = ref _registry.Entries[_registry.Count];
                entry.HashCode = hashCode;
                entry.Key.Type = type;
                entry.Key.Name = name;
                entry.Next = _registry.Buckets[targetBucket];
                entry.Value = registration;
                _registry.Buckets[targetBucket] = _registry.Count++;

                return (ImplicitRegistration)entry.Value;
            }
        }

        private IEnumerable<IPolicySet> AddOrReplaceRegistrations(IEnumerable<Type> interfaces, string name, ExplicitRegistration registration)
        {
            int count = 0;

            if (null != interfaces)
            {
                foreach (var type in interfaces)
                {
                    // Add or replace existing 
                    var previous = Register(type, name, registration);
                    if (null != previous) yield return previous;

                    count++;
                }
            }

            if (0 == count)
            {
                // TODO: Move to diagnostic
                if (null == registration.Type) throw new ArgumentNullException(nameof(interfaces));

                // Add or replace existing 
                var previous = Register(registration.Type, name, registration);
                if (null != previous) yield return previous;
            }
        }

        #endregion


        #region Creating Implicit Registration

        private ImplicitRegistration CreateRegistration(Type type, IPolicySet factory)
        {
            var registration = new ImplicitRegistration(factory);

            registration.BuildChain = _strategiesChain.Where(strategy => strategy.RequiredToBuildType(this, type, registration, null))
                                                      .ToArray();

            if (registration.LifetimeManager is IDisposable)
                LifetimeContainer.Add(registration.LifetimeManager);

            return registration;
        }

        #endregion
    }
}
