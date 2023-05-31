using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using WebApiModulum.Container;
using WebApiModulum.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Text;
using System.Security.Claims;
using WebApiModulum.Handler;

namespace WebApiModulum.Controllers;

[ApiController]
[Route("[controller]")]
public class UserController : ControllerBase
{
    private readonly ModulumContext _dbContext;
    private readonly JwtSettings jwtsettings;
    private readonly IRefreshTokenGenerator refreshTokenGenerator;

    [NonAction]
    public DateTime getDateNow()
    {
        return DateTime.Now.AddHours(3).AddSeconds(20);     // Comentar essa linha antes de subir o fonte
        //return DateTime.Now.AddSeconds(20);
    }
    public UserController(ModulumContext dbContext, IOptions<JwtSettings> options, IRefreshTokenGenerator refresh)
    {
        this._dbContext = dbContext;
        this.jwtsettings = options.Value;
        this.refreshTokenGenerator = refresh;
    }

    [NonAction]
    public async Task<TokenResponse> tokenAuthenticate(string user, Claim[] claims)
    {
        var token = new JwtSecurityToken
        (
            claims:claims, 
            expires:getDateNow(),
            signingCredentials: new SigningCredentials
            (
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtsettings.securitykey)),
                SecurityAlgorithms.HmacSha256
            )
        );
        var jwttoken = new JwtSecurityTokenHandler().WriteToken(token);
        return new TokenResponse()
        {
            jwttoken = jwttoken,
            refreshtoken = await refreshTokenGenerator.GenerateToken(user)
        };
    }

    [HttpPost("Authenticate")]
    public async Task<IActionResult> Authenticate([FromBody]UserCred userCred)
    {
        var user = await this._dbContext.Usuario.FirstOrDefaultAsync(item=> item.Login == userCred.username && item.Senha == userCred.password);
        if(user == null)
        {
            return Unauthorized();
        }
        var tokenhandler = new JwtSecurityTokenHandler();
        var tokenkey = Encoding.UTF8.GetBytes(this.jwtsettings.securitykey);
        var tokendesc = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity
            (
                new Claim[] {new Claim(ClaimTypes.Name, user.Login), new Claim(ClaimTypes.Role, user.TpUsuario)}
            ),
            Expires = getDateNow(),
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(tokenkey), SecurityAlgorithms.HmacSha256)
        };
        var token = tokenhandler.CreateToken(tokendesc);
        string finaltoken = tokenhandler.WriteToken(token);

        var response = new TokenResponse()
        {
            jwttoken = finaltoken,
            refreshtoken = await refreshTokenGenerator.GenerateToken(userCred.username)
        };

        return Ok(response);
    }

    [HttpPost("RefreshToken")]
    public async Task<IActionResult> RefreshToken([FromBody]TokenResponse tokenResponse)
    {
        var tokenhandler = new JwtSecurityTokenHandler();
        var tokenkey = Encoding.UTF8.GetBytes(this.jwtsettings.securitykey);
        SecurityToken securityToken; 
        var principal = tokenhandler.ValidateToken(tokenResponse.jwttoken, new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(tokenkey),
            ValidateIssuer = false,
            ValidateAudience = false
        }, out securityToken);

        var token = securityToken as JwtSecurityToken;
        if(token != null && !token.Header.Alg.Equals(SecurityAlgorithms.HmacSha256))
        {
            return Unauthorized();
        }
        var username = principal.Identity?.Name;
        var user = await this._dbContext.RefreshToken.FirstOrDefaultAsync(item=> item.loginUsu == username && item.refreshToken == tokenResponse.refreshtoken);
        if(user == null)
        {
            return Unauthorized();
        }
        var response = tokenAuthenticate(username, principal.Claims.ToArray()).Result;

        return Ok(response);
    }
}