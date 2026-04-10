using Binner.Model.Configuration;

namespace Binner.PrintSpoolService
{
    public class PrintConfiguration
    {
        /// <summary>
        /// The public uri for Binner.io
        /// </summary>
        private string _publicUrl = string.Empty;
        public string PublicUrl
        {
            get
            {
                if (!string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable(EnvironmentVarConstants.PublicUrl)))
                    return System.Environment.GetEnvironmentVariable(EnvironmentVarConstants.PublicUrl);
                return _publicUrl;
            }
            set
            {
                _publicUrl = value;
            }
        }

        /// <summary>
        /// Your unique print spool queue id (see Settings => Organization Configuration => PrintSpoolQueueId)
        /// </summary>
        private Guid _printSpoolQueueId = Guid.Empty;
        public Guid PrintSpoolQueueId
        {
            get
            {
                if (!string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable(EnvironmentVarConstants.PrintSpoolQueueId)))
                    if (Guid.TryParse(System.Environment.GetEnvironmentVariable(EnvironmentVarConstants.PrintSpoolQueueId), out var value))
                        return value;
                return _printSpoolQueueId;
            }
            set
            {
                _printSpoolQueueId = value;
            }
        }
    }
}
