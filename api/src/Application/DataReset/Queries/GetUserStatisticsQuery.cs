using Application.Common.Interfaces;
using MediatR;

namespace Application.DataReset.Queries;

public record GetUserStatisticsQuery : IRequest<UserStatistics>;

public class GetUserStatisticsQueryHandler(IDataResetService dataResetService)
    : IRequestHandler<GetUserStatisticsQuery, UserStatistics>
{
    public Task<UserStatistics> Handle(GetUserStatisticsQuery request, CancellationToken cancellationToken)
        => dataResetService.GetUserStatisticsAsync(cancellationToken);
}