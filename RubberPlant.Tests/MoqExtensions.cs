using System.Collections.Generic;
using Moq.Language.Flow;

namespace RubberPlant.Tests
{
    // Taken from http://haacked.com/archive/2009/09/29/moq-sequences.aspx/
    public static class MoqExtensions
    {
        public static void ReturnsInOrder<T, TResult>(this ISetup<T, TResult> setup, params TResult[] results) where T : class
        {
            setup.Returns(new Queue<TResult>(results).Dequeue);
        }
    }
}
