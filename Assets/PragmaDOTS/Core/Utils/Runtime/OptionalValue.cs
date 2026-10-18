namespace Pragma.Dots.Utils
{
    /// <summary>An unmanaged value that may be absent: a <see cref="System.Nullable{T}"/> that fits in components.</summary>
    public struct OptionalValue<T> where T : unmanaged
    {
        private bool _isSet;
        private T _value;

        public OptionalValue(T value)
        {
            _value = value;
            _isSet = true;
        }

        /// <summary>The value; <c>default</c> while it is not set. Assigning it sets it.</summary>
        public T Value
        {
            get => _value;
            set
            {
                _value = value;
                _isSet = true;
            }
        }

        public bool IsSet => _isSet;

        public T? ToNullable()
        {
            return _isSet ? _value : (T?)null;
        }

        public static OptionalValue<T> FromNullable(T? value)
        {
            return value.HasValue ? new OptionalValue<T>(value.Value) : default;
        }
    }
}
