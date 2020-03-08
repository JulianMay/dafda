using System.Threading.Tasks;
using Dafda.Consuming;
using Microsoft.Extensions.Logging;
using Outbox.Domain;

namespace Outbox.Application
{
    internal class StudentEnrolledHandler : IMessageHandler<StudentEnrolled>
    {
        private readonly ILogger<StudentEnrolledHandler> _logger;
        private readonly Stats _stats;

        public StudentEnrolledHandler(ILogger<StudentEnrolledHandler> logger, Stats stats)
        {
            _logger = logger;
            _stats = stats;
        }

        public Task Handle(StudentEnrolled message, MessageHandlerContext context)
        {
            _logger.LogDebug($@"{this.GetType().Name} handled: {{@Message}}", message);

            _stats.Consume();
            
            _logger.LogInformation("{Stats}", _stats.ToString());

            return Task.CompletedTask;
        }
    }
}