using System;

namespace RunningOrders.Model
{
    internal class QueueTickException(string message, Exception innerException) : Exception(message, innerException)
    {
    }
}
