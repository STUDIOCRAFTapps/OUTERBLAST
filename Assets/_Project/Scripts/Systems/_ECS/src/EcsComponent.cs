// ----------------------------------------------------------------------------
// The MIT License
// Simple Entity Component System framework https://github.com/Leopotam/ecs
// Copyright (c) 2017-2020 Leopotam <leopotam@gmail.com>
// ----------------------------------------------------------------------------

using System;
using System.Runtime.CompilerServices;
using System.Threading;

// ReSharper disable ClassNeverInstantiated.Global

namespace Blast.ECS {
    /// <summary>
    /// Marks component type to be not auto-filled as GetX in filter.
    /// </summary>
    public interface IEcsIgnoreInFilter { }

    /// <summary>
    /// Marks component type for custom reset behaviour.
    /// </summary>
    /// <typeparam name="T">Type of component, should be the same as main component!</typeparam>
    public interface IEcsAutoReset<T> where T : struct {
        void AutoReset (ref T c);
    }

    /// <summary>
    /// Marks field of IEcsSystem class to be ignored during dependency injection.
    /// </summary>
    public sealed class EcsIgnoreInjectAttribute : Attribute { }

    /// <summary>
    /// Global descriptor of used component type.
    /// </summary>
    /// <typeparam name="T">Component type.</typeparam>
    public static class EcsComponentType<T> where T : struct {
        // ReSharper disable StaticMemberInGenericType
        public static readonly int TypeIndex;
        public static readonly Type Type;
        public static readonly bool IsIgnoreInFilter;
        public static readonly bool IsAutoReset;
        // ReSharper restore StaticMemberInGenericType

        static EcsComponentType () {
            TypeIndex = Interlocked.Increment (ref EcsComponentPool.ComponentTypesCount);
            Type = typeof (T);
            IsIgnoreInFilter = typeof (IEcsIgnoreInFilter).IsAssignableFrom (Type);
            IsAutoReset = typeof (IEcsAutoReset<T>).IsAssignableFrom (Type);
#if DEBUG
            if (!IsAutoReset && Type.GetInterface ("IEcsAutoReset`1") != null) {
                throw new Exception ($"IEcsAutoReset should have <{typeof (T).Name}> constraint for component \"{typeof (T).Name}\".");
            }
#endif
        }
    }

    public sealed class EcsComponentPool {
        /// <summary>
        /// Global component type counter.
        /// First component will be "1" for correct filters updating (add component on positive and remove on negative).
        /// </summary>
        internal static int ComponentTypesCount;
    }

    public interface IEcsComponentPool {
        Type ItemType { get; }
        object GetItem (int idx);
        void Recycle (int idx);
        int New ();
        void CopyData (int srcIdx, int dstIdx);
    }

    /// <summary>
    /// Helper for save reference to component. 
    /// </summary>
    /// <typeparam name="T">Type of component.</typeparam>
    public struct EcsComponentRef<T> where T : struct {
        internal EcsComponentPool<T> Pool;
        internal int Idx;

        [MethodImpl (MethodImplOptions.AggressiveInlining)]
        public static bool operator == (in EcsComponentRef<T> lhs, in EcsComponentRef<T> rhs) {
            return lhs.Idx == rhs.Idx && lhs.Pool == rhs.Pool;
        }

        [MethodImpl (MethodImplOptions.AggressiveInlining)]
        public static bool operator != (in EcsComponentRef<T> lhs, in EcsComponentRef<T> rhs) {
            return lhs.Idx != rhs.Idx || lhs.Pool != rhs.Pool;
        }

        public override bool Equals (object obj) {
            return obj is EcsComponentRef<T> other && Equals (other);
        }

        [MethodImpl (MethodImplOptions.AggressiveInlining)]
        public override int GetHashCode () {
            // ReSharper disable NonReadonlyMemberInGetHashCode
            return Idx;
            // ReSharper restore NonReadonlyMemberInGetHashCode
        }
    }

#if ENABLE_IL2CPP
    [Unity.IL2CPP.CompilerServices.Il2CppSetOption (Unity.IL2CPP.CompilerServices.Option.NullChecks, false)]
    [Unity.IL2CPP.CompilerServices.Il2CppSetOption (Unity.IL2CPP.CompilerServices.Option.ArrayBoundsChecks, false)]
#endif
    public static class EcsComponentRefExtensions {
        [MethodImpl (MethodImplOptions.AggressiveInlining)]
        public static ref T Unref<T> (in this EcsComponentRef<T> wrapper) where T : struct {
            return ref wrapper.Pool.Items[wrapper.Idx];
        }

        [MethodImpl (MethodImplOptions.AggressiveInlining)]
        public static bool IsNull<T> (in this EcsComponentRef<T> wrapper) where T : struct {
            return wrapper.Pool == null;
        }
    }

#if ENABLE_IL2CPP
    [Unity.IL2CPP.CompilerServices.Il2CppSetOption (Unity.IL2CPP.CompilerServices.Option.NullChecks, false)]
    [Unity.IL2CPP.CompilerServices.Il2CppSetOption (Unity.IL2CPP.CompilerServices.Option.ArrayBoundsChecks, false)]
#endif
    public sealed class EcsComponentPool<T> : IEcsComponentPool where T : struct {
        /// <summary>
        /// Description of custom AutoReset handler.
        /// </summary>
        public delegate void AutoResetHandler (ref T component);

        delegate void AutoResetHandler2 (ref T component);

        public Type ItemType { get; }
        public T[] Items = new T[128];
        int[] _reservedItems = new int[128];
        int _itemsCount;
        int _reservedItemsCount;
        readonly AutoResetHandler2 _autoReset;

        internal EcsComponentPool () {
            ItemType = typeof (T);
            if (EcsComponentType<T>.IsAutoReset) {
                var autoResetMethod = typeof (T).GetMethod (nameof (IEcsAutoReset<T>.AutoReset));
#if DEBUG

                if (autoResetMethod == null) {
                    throw new Exception (
                        $"IEcsAutoReset<{typeof (T).Name}> explicit implementation not supported, use implicit instead.");
                }
#endif
                _autoReset = (AutoResetHandler2) Delegate.CreateDelegate (
                    typeof (AutoResetHandler2),
                    null,
                    autoResetMethod);
            }
        }

        /// <summary>
        /// Sets new capacity (if more than current amount).
        /// </summary>
        /// <param name="capacity">New value.</param>
        public void SetCapacity (int capacity) {
            if (capacity > Items.Length) {
                Array.Resize (ref Items, capacity);
            }
        }

        [MethodImpl (MethodImplOptions.AggressiveInlining)]
        public int New () {
            int id;
            if (_reservedItemsCount > 0) {
                id = _reservedItems[--_reservedItemsCount];
            } else {
                id = _itemsCount;
                if (_itemsCount == Items.Length) {
                    Array.Resize (ref Items, _itemsCount << 1);
                }
                // reset brand new instance if custom AutoReset was registered.
                _autoReset?.Invoke (ref Items[_itemsCount]);
                _itemsCount++;
            }
            return id;
        }

        [MethodImpl (MethodImplOptions.AggressiveInlining)]
        public ref T GetItem (int idx) {
            return ref Items[idx];
        }

        [MethodImpl (MethodImplOptions.AggressiveInlining)]
        public void Recycle (int idx) {
            if (_autoReset != null) {
                _autoReset (ref Items[idx]);
            } else {
                Items[idx] = default;
            }
            if (_reservedItemsCount == _reservedItems.Length) {
                Array.Resize (ref _reservedItems, _reservedItemsCount << 1);
            }
            _reservedItems[_reservedItemsCount++] = idx;
        }

        [MethodImpl (MethodImplOptions.AggressiveInlining)]
        public void CopyData (int srcIdx, int dstIdx) {
            Items[dstIdx] = Items[srcIdx];
        }

        [MethodImpl (MethodImplOptions.AggressiveInlining)]
        public EcsComponentRef<T> Ref (int idx) {
            EcsComponentRef<T> componentRef;
            componentRef.Pool = this;
            componentRef.Idx = idx;
            return componentRef;
        }

        object IEcsComponentPool.GetItem (int idx) {
            return Items[idx];
        }
    }
}