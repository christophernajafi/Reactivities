using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Application.errors;
using Domain;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Persistence;

namespace Application.User
{
    public class Login
    {
        public class Query : IRequest<User>
        {
            public string Email { get; set; }
            public string Password { get; set; }
        }

        public class QueryValidator : AbstractValidator<Query>
        {
            public QueryValidator()
            {
                RuleFor(x => x.Email).NotEmpty();
                RuleFor(x => x.Password).NotEmpty();
            }
        }

        public class Handler : IRequestHandler<Query, User>
        {
            private readonly UserManager<User> _userManager;
            private readonly SignInManager<User> _signInManager;
            public Handler(UserManager<User> userManager, SignInManager<User> signInManager)
            {
                _signInManager = signInManager;
                _userManager = userManager;
            }

            public async Task<User> Handle(Query request, CancellationToken cancellationToken)
            {
                var user = await _userManager.FindByEmailAsync(request.Email);

                if (user == null)
                    throw new RestException(HttpStatusCode.Unauthorized);

                var result = await _signInManager.CheckPasswordSignInAsync(user, request.Password, false);

                if (result.Succeeded)
                {
                    // TODO: generate token
                    return new User
                    {
                        DisplayName = user.DisplayName,
                        Token = "This will be a token",

                        // When I type "Username = user.UserName,", I get an error here
                        // Username = user.UserName,
                        Username = user.Username,
                        Image = null
                    };
                }

                throw new RestException(HttpStatusCode.Unauthorized);
            }
        }
    }
}