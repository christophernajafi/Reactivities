using System;
using System.Threading.Tasks;
using System.Text;
using API.Middleware;
using Application.Activities;
using Application.Interfaces;
using AutoMapper;
using Domain;
using FluentValidation.AspNetCore;
using Infrastructure.Photos;
using Infrastructure.Security;
using MediatR;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using Persistence;
using API.SignalR;
using Application.Profiles;
using Infrastructure.Email;

namespace API
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureDevelopmentServices(IServiceCollection services)
        {
            services.AddDbContext<DataContext>(opt =>
             {
                 opt.UseLazyLoadingProxies();
                 opt.UseSqlite(Configuration.GetConnectionString("DefaultConnection"));
                 //  opt.UseNpgsql(Configuration.GetConnectionString("DefaultConnection"));
             });

            ConfigureServices(services);
        }

        public void ConfigureProductionServices(IServiceCollection services)
        {
            services.AddDbContext<DataContext>(opt =>
             {
                 opt.UseLazyLoadingProxies();
                //  opt.UseSqlServer(Configuration.GetConnectionString("DefaultConnection"));
                 //  opt.UseMySql(Configuration.GetConnectionString("DefaultConnection"));
                 //  opt.UseNpgsql(Configuration.GetConnectionString("DefaultConnection"));
             });

            // comment back in when converting to Postgres
            // services.AddDbContext<DataContext>(options =>
            // {
            //     var env = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");

            //     string connStr;

            //     // Depending on if in development or production, use either Heroku-provided
            //     // connection string, or development connection string from env var.
            //     if (env == "Development")
            //     {
            //         // Use connection string from file.
            //         connStr = config.GetConnectionString("DefaultConnection");
            //     }
            //     else
            //     {
            //         // Use connection string provided at runtime by Heroku.
            //         var connUrl = Environment.GetEnvironmentVariable("DATABASE_URL");

            //         // Parse connection URL to connection string for Npgsql
            //         connUrl = connUrl.Replace("postgres://", string.Empty);
            //         var pgUserPass = connUrl.Split("@")[0];
            //         var pgHostPortDb = connUrl.Split("@")[1];
            //         var pgHostPort = pgHostPortDb.Split("/")[0];
            //         var pgDb = pgHostPortDb.Split("/")[1];
            //         var pgUser = pgUserPass.Split(":")[0];
            //         var pgPass = pgUserPass.Split(":")[1];
            //         var pgHost = pgHostPort.Split(":")[0];
            //         var pgPort = pgHostPort.Split(":")[1];

            //         connStr = $"Server={pgHost};Port={pgPort};User Id={pgUser};Password={pgPass};Database={pgDb}; SSL Mode=Require; Trust Server Certificate=true";
            //     }

            //     // Whether the connection string came from the local development configuration file
            //     // or from the environment variable from Heroku, use it to set up your DbContext.
            //     options.UseNpgsql(connStr);
            // }); // END Postgres

            ConfigureServices(services);
        }

        public void ConfigureServices(IServiceCollection services)
        {
            // services.AddDbContext<DataContext>(opt =>
            // {
            //     opt.UseLazyLoadingProxies();
            //     opt.UseSqlite(Configuration.GetConnectionString("DefaultConnection"));
            // });
            services.AddCors(opt =>
            {
                opt.AddPolicy("CorsPolicy", policy =>
                 {
                     policy.AllowAnyHeader()
                     .AllowAnyMethod()
                     .WithExposedHeaders("WWW-Authenticate")
                     .WithOrigins("http://localhost:3000")
                     .AllowCredentials();
                 });
            });
            services.AddMediatR(typeof(List.Handler).Assembly);
            services.AddAutoMapper(typeof(List.Handler));
            services.AddSignalR();
            services.AddControllers(opt =>
            {
                var policy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
                opt.Filters.Add(new AuthorizeFilter(policy));
            }).AddFluentValidation(cfg =>
            {
                cfg.RegisterValidatorsFromAssemblyContaining<Create>();
            });

            var builder = services.AddIdentityCore<AppUser>(options =>
            {
                options.SignIn.RequireConfirmedEmail = true;
            });
            var identityBuilder = new IdentityBuilder(builder.UserType, builder.Services);
            identityBuilder.AddEntityFrameworkStores<DataContext>();
            identityBuilder.AddSignInManager<SignInManager<AppUser>>();
            identityBuilder.AddDefaultTokenProviders();

            services.AddAuthorization(opt =>
            {
                opt.AddPolicy("IsActivityHost", policy =>
                 {
                     policy.Requirements.Add(new IsHostRequirement());
                 });
            });
            services.AddTransient<IAuthorizationHandler, IsHostRequirementHandler>();

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Configuration["TokenKey"]));

            services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(opt =>
            {
                opt.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = key,
                    ValidateAudience = false,
                    ValidateIssuer = false,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.Zero
                };
                opt.Events = new JwtBearerEvents
                {
                    OnMessageReceived = context =>
                      {
                          var accessToken = context.Request.Query["access_token"];
                          var path = context.HttpContext.Request.Path;
                          if (!string.IsNullOrEmpty(accessToken)
                            && (path.StartsWithSegments("/chat")))
                          {
                              context.Token = accessToken;
                          }
                          return Task.CompletedTask;
                      }
                };
            });

            services.AddScoped<IJwtGenerator, JwtGenerator>();
            services.AddScoped<IUserAccessor, UserAccessor>();
            services.AddScoped<IPhotoAccessor, PhotoAccessor>();
            services.AddScoped<IProfileReader, ProfileReader>();
            services.AddScoped<IFacebookAccessor, FacebookAccessor>();
            services.AddScoped<IEmailSender, EmailSender>();
            services.Configure<CloudinarySettings>(Configuration.GetSection("Cloudinary"));
            services.Configure<FacebookAppSettings>(Configuration.GetSection("Authentication:Facebook"));
            services.Configure<SendGridSettings>(Configuration.GetSection("SendGrid"));
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            app.UseMiddleware<ErrorHandlingMiddleware>();
            if (env.IsDevelopment())
            {
                // app.UseDeveloperExceptionPage();
            }

            // Uncomment before deploying to Azure
            app.UseHttpsRedirection();

            // Security headers
            app.UseXContentTypeOptions();
            app.UseReferrerPolicy(opt => opt.NoReferrer());
            app.UseXXssProtection(opt => opt.EnabledWithBlockMode());
            app.UseXfo(opt => opt.Deny());
            app.UseCsp(opt => opt
                    .BlockAllMixedContent()
                    .StyleSources(s => s.Self()
                        .CustomSources("https://fonts.googleapis.com", "sha256-F4GpCPyRepgP5znjMD8sc7PEjzet5Eef4r09dEGPpTs="))
                    .FontSources(s => s.Self().CustomSources("https://fonts.gstatic.com", "data:"))
                    .FormActions(s => s.Self())
                    .FrameAncestors(s => s.Self())
                    .ImageSources(s => s.Self().CustomSources("https://res.cloudinary.com", "blob:", "data:"))
                // .ScriptSources(s => s.Self().CustomSources("sha256-zTmokOtDNMlBIULqs//ZgFtzokerG72Q30ccMjdGbSA="))
                );

            app.UseRouting();
            
            app.UseDefaultFiles();
            app.UseStaticFiles();

            app.UseCors("CorsPolicy");

            app.UseAuthentication();
            app.UseAuthorization();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
                endpoints.MapHub<ChatHub>("/chat");
                endpoints.MapFallbackToController("Index", "Fallback");
            });
        }
    }
}
