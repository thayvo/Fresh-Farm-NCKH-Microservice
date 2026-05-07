using Microsoft.AspNetCore.Identity;

if (args.Length == 0)
{
    Console.Error.WriteLine("Usage: recommendation-buyagain-verify <password>");
    return 1;
}

var password = args[0];
var hasher = new PasswordHasher<object>();
Console.WriteLine(hasher.HashPassword(new object(), password));
return 0;
