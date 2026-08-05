using Api.Dtos;
using Application.Common.Permissions;

namespace Api.Services.Abstract;

public interface IFundingDashboardControllerService
{
    Task<FundingDashboardDto> GetDashboard(Permission permission, CancellationToken cancellationToken);
}