using Microsoft.FSharp.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Rocks.Csharp
{
    public static class OptionExtensions
    {
        public static bool IsNone<T>(this FSharpOption<T> option)
        {
            return FSharpOption<T>.get_IsNone(option);
        }

        public static bool IsSome<T>(this FSharpOption<T> option)
        {
            return FSharpOption<T>.get_IsSome(option);
        }

        public static FSharpOption<T> ToOption<T>(this T thing)
            where T : class
        {
            if (thing == null)
                return FSharpOption<T>.None;
            else
                return new FSharpOption<T>(thing);
        }

        public static FSharpOption<T> Some<T>(this T thing)
            where T : struct
        {
            return new FSharpOption<T>(thing);
        }

        public static FSharpOption<T> None<T>(this T thing)
            where T : struct
        {
            return FSharpOption<T>.None;
        }
    }
}
