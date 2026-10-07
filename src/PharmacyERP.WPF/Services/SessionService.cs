using PharmacyERP.Application.Common.Interfaces;
using PharmacyERP.Application.Features.Auth.DTOs;

namespace PharmacyERP.WPF.Services;

public class SessionService : ISessionService
{
    private readonly ICurrentUserService _currentUserService;

    public SessionService(ICurrentUserService currentUserService)
    {
        _currentUserService = currentUserService;
    }

    public bool IsAuthenticated => CurrentSession is not null;
    public LoginResultDto? CurrentSession { get; private set; }

    public void Start(LoginResultDto session)
    {
        CurrentSession = session;
        _currentUserService.SetSession(session.UserId, session.Username, session.BranchId, session.Permissions);
    }

    public void End()
    {
        CurrentSession = null;
        _currentUserService.ClearSession();
    }
}
