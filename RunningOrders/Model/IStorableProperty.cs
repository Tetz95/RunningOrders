using KeyValue.Runtime;

namespace RunningOrders.Model
{
    public interface IStorableProperty
    {
        public abstract Value ToPropertyValue();
    }
}
