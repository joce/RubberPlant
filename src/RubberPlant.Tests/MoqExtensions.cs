using System;
using System.Collections;
using Moq.Language.Flow;

namespace RubberPlant.Tests
{
    public static class MoqExtensions
    {
        // Taken from http://haacked.com/archive/2010/11/24/moq-sequences-revisited.aspx/
        public static void ReturnsInOrder<T, TResult>(this ISetup<T, TResult> setup, params object[] results) where T : class
        {
            var queue = new Queue(results);
            setup.Returns(() =>
            {
                var result = queue.Dequeue();
                if (result is Exception)
                {
                    throw result as Exception;
                }
                return (TResult)result;
            });
        }
    }
}
