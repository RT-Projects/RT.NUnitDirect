using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

namespace NUnit.Direct
{
    static class Extensions
    {
        /// <summary>
        /// Formats a string using <see cref="string.Format(string, object[])"/>.
        /// </summary>
        public static string Fmt(this string formatString, params object[] args)
        {
            return string.Format(formatString, args);
        }

        /// <summary>
        /// Formats a string using <see cref="string.Format(string, object)"/>.
        /// </summary>
        public static string Fmt(this string formatString, object arg0)
        {
            return string.Format(formatString, arg0);
        }

        /// <summary>
        /// Formats a string using <see cref="string.Format(string, object, object)"/>.
        /// </summary>
        public static string Fmt(this string formatString, object arg0, object arg1)
        {
            return string.Format(formatString, arg0, arg1);
        }

        /// <summary>
        /// Formats a string using <see cref="string.Format(string, object, object, object)"/>.
        /// </summary>
        public static string Fmt(this string formatString, object arg0, object arg1, object arg2)
        {
            return string.Format(formatString, arg0, arg1, arg2);
        }

        /// <summary>Adds a single element to the end of an IEnumerable.</summary>
        /// <typeparam name="T">Type of enumerable to return.</typeparam>
        /// <returns>IEnumerable containing all the input elements, followed by the specified additional element.</returns>
        public static IEnumerable<T> Concat<T>(this IEnumerable<T> source, T element)
        {
            if (source == null)
                throw new ArgumentNullException("source");
            return concatIterator(element, source, false);
        }

        private static IEnumerable<T> concatIterator<T>(T extraElement, IEnumerable<T> source, bool insertAtStart)
        {
            if (insertAtStart)
                yield return extraElement;
            foreach (var e in source)
                yield return e;
            if (!insertAtStart)
                yield return extraElement;
        }

        /// <summary>
        /// <para>Turns all elements in the enumerable to strings and joins them using the specified string
        /// as the separator and the specified prefix and suffix for each string.</para>
        /// <example>
        ///     <code>
        ///         var a = (new[] { "Paris", "London", "Tokyo" }).JoinString(", ", "[", "]");
        ///         // a contains "[Paris], [London], [Tokyo]"
        ///         ...JoinString(", ", "[", "]", " and ");
        ///         // a contains "[Paris], [London] and [Tokyo]"
        ///     </code>
        /// </example>
        /// </summary>
        public static string JoinString<T>(this IEnumerable<T> values, string separator = null, string prefix = null, string suffix = null, string lastSeparator = null)
        {
            if (values == null)
                throw new ArgumentNullException("values");
            if (lastSeparator == null)
                lastSeparator = separator;

            using (var enumerator = values.GetEnumerator())
            {
                if (!enumerator.MoveNext())
                    return "";

                // Optimise the case where there is only one element
                var one = enumerator.Current;
                if (!enumerator.MoveNext())
                    return prefix + one + suffix;

                // Optimise the case where there are only two elements
                var two = enumerator.Current;
                if (!enumerator.MoveNext())
                {
                    // Optimise the (common) case where there is no prefix/suffix; this prevents an array allocation when calling string.Concat()
                    if (prefix == null && suffix == null)
                        return one + lastSeparator + two;
                    return prefix + one + suffix + lastSeparator + prefix + two + suffix;
                }

                StringBuilder sb = new StringBuilder()
                    .Append(prefix).Append(one).Append(suffix).Append(separator)
                    .Append(prefix).Append(two).Append(suffix);
                var prev = enumerator.Current;
                while (enumerator.MoveNext())
                {
                    sb.Append(separator).Append(prefix).Append(prev).Append(suffix);
                    prev = enumerator.Current;
                }
                sb.Append(lastSeparator).Append(prefix).Append(prev).Append(suffix);
                return sb.ToString();
            }
        }

        /// <summary>
        /// Returns all fields contained in the specified type, including private fields inherited from base classes.
        /// </summary>
        /// <param name="type">The type to return all fields of.</param>
        /// <returns>An <see cref="IEnumerable&lt;FieldInfo&gt;"/> containing all fields contained in this type, including private fields inherited from base classes.</returns>
        public static IEnumerable<FieldInfo> GetAllFields(this Type type)
        {
            IEnumerable<FieldInfo> fields = type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            var baseType = type.BaseType;
            return (baseType == null) ? fields : GetAllFields(baseType).Concat(fields);
        }

    }
}