using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Identity.Client;
using AiReviewer.VSIX.Configuration;

namespace AiReviewer.VSIX.Services
{
    /// <summary>
    /// Handles Azure AD authentication for the extension.
    /// Uses MSAL (Microsoft Authentication Library) to acquire tokens.
    /// 
    /// Flow:
    /// 1. Try to get token silently (from cache)
    /// 2. If no cached token, prompt user to sign in interactively
    /// 3. Token is automatically cached and refreshed
    /// </summary>
    public class AzureAdAuthService
    {
        private static AzureAdAuthService _instance;
        private static readonly object _lock = new object();

        private readonly IPublicClientApplication _msalClient;
        private readonly string[] _scopes;

        /// <summary>
        /// Singleton instance of the auth service
        /// </summary>
        public static AzureAdAuthService Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                        {
                            _instance = new AzureAdAuthService();
                        }
                    }
                }
                return _instance;
            }
        }

        private AzureAdAuthService()
        {
            // Configure MSAL public client application
            // For desktop apps with browser auth, must use loopback redirect
            _msalClient = PublicClientApplicationBuilder
                .Create(AppConfig.AzureAdClientId)
                .WithAuthority(AppConfig.AzureAdAuthority)
                .WithRedirectUri("http://localhost")  // loopback required for interactive auth
                .WithClientName("AI Code Reviewer")
                .WithClientVersion("1.4.0")
                .Build();

            // The scope we need to access our API
            _scopes = new[] { AppConfig.AzureAdScope };

            // Enable token caching (persists across sessions)
            TokenCacheHelper.EnableSerialization(_msalClient.UserTokenCache);
            
            System.Diagnostics.Debug.WriteLine($"[Auth] Initialized with ClientId: {AppConfig.AzureAdClientId}");
            System.Diagnostics.Debug.WriteLine($"[Auth] Scope: {AppConfig.AzureAdScope}");
            System.Diagnostics.Debug.WriteLine($"[Auth] Authority: {AppConfig.AzureAdAuthority}");
        }

        /// <summary>
        /// Gets a valid access token, using cache if available.
        /// Will prompt user to sign in if needed.
        /// </summary>
        /// <returns>Access token string, or null if authentication failed</returns>
        public async Task<string> GetAccessTokenAsync()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("[Auth] GetAccessTokenAsync called - starting authentication flow...");
                
                // Try to get token silently first (from cache)
                var accounts = await _msalClient.GetAccountsAsync();
                var firstAccount = accounts.FirstOrDefault();

                System.Diagnostics.Debug.WriteLine($"[Auth] Found {accounts.Count()} cached accounts");

                if (firstAccount != null)
                {
                    try
                    {
                        System.Diagnostics.Debug.WriteLine($"[Auth] Trying silent auth for {firstAccount.Username}...");
                        var silentResult = await _msalClient
                            .AcquireTokenSilent(_scopes, firstAccount)
                            .ExecuteAsync();

                        System.Diagnostics.Debug.WriteLine($"[Auth] Got token silently for {silentResult.Account.Username}");
                        return silentResult.AccessToken;
                    }
                    catch (MsalUiRequiredException)
                    {
                        // Token expired or needs refresh - will fall through to interactive
                        System.Diagnostics.Debug.WriteLine("[Auth] Silent token acquisition failed, will try interactive");
                    }
                }

                // No cached token or cache expired - need interactive login
                System.Diagnostics.Debug.WriteLine("[Auth] Starting INTERACTIVE login - browser should open...");
                
                try
                {
                    var interactiveResult = await _msalClient
                        .AcquireTokenInteractive(_scopes)
                        .WithPrompt(Prompt.SelectAccount)
                        .WithUseEmbeddedWebView(false) 
                        .ExecuteAsync();

                    System.Diagnostics.Debug.WriteLine($"[Auth] Got token interactively for {interactiveResult.Account.Username}");
                    return interactiveResult.AccessToken;
                }
                catch (MsalClientException clientEx) when (clientEx.ErrorCode == "authentication_canceled")
                {
                    System.Diagnostics.Debug.WriteLine("[Auth] User canceled authentication");
                    throw new InvalidOperationException("Authentication was canceled. Please sign in to use AI Code Reviewer.");
                }
            }
            catch (MsalServiceException serviceEx)
            {
                System.Diagnostics.Debug.WriteLine($"[Auth] MSAL service error: {serviceEx.ErrorCode} - {serviceEx.Message}");
                throw new InvalidOperationException($"Authentication service error: {serviceEx.Message}\n\nPlease check your Azure AD configuration.");
            }
            catch (MsalException ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Auth] MSAL error: {ex.ErrorCode} - {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"[Auth] Full exception: {ex}");
                throw new InvalidOperationException($"Authentication failed: {ex.Message}");
            }
            catch (InvalidOperationException)
            {
                throw; // Re-throw our custom exceptions
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Auth] Error getting token: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"[Auth] Full exception: {ex}");
                throw new InvalidOperationException($"Authentication error: {ex.Message}");
            }
        }

        /// <summary>
        /// Signs out the current user (clears token cache)
        /// </summary>
        public async Task SignOutAsync()
        {
            var accounts = await _msalClient.GetAccountsAsync().ConfigureAwait(false);
            foreach (var account in accounts)
            {
                await _msalClient.RemoveAsync(account).ConfigureAwait(false);
                System.Diagnostics.Debug.WriteLine($"[Auth] Signed out {account.Username}");
            }
        }

        /// <summary>
        /// Gets the currently signed-in user's email, or null if not signed in
        /// </summary>
        public async Task<string> GetCurrentUserAsync()
        {
            var accounts = await _msalClient.GetAccountsAsync().ConfigureAwait(false);
            return accounts.FirstOrDefault()?.Username;
        }

        /// <summary>
        /// Checks if there's a cached token (user previously signed in)
        /// </summary>
        public async Task<bool> IsSignedInAsync()
        {
            var accounts = await _msalClient.GetAccountsAsync().ConfigureAwait(false);
            return accounts.Any();
        }
    }

    /// <summary>
    /// Helper class to persist MSAL token cache to disk.
    /// Tokens survive VS restarts - users don't need to sign in every time.
    /// </summary>
    internal static class TokenCacheHelper
    {
        private static readonly string CacheFilePath = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AiCodeReviewer",
            "msal_token_cache.dat");

        private static readonly object FileLock = new object();

        public static void EnableSerialization(ITokenCache tokenCache)
        {
            tokenCache.SetBeforeAccess(BeforeAccessNotification);
            tokenCache.SetAfterAccess(AfterAccessNotification);
        }

        private static void BeforeAccessNotification(TokenCacheNotificationArgs args)
        {
            lock (FileLock)
            {
                try
                {
                    if (System.IO.File.Exists(CacheFilePath))
                    {
                        var data = System.IO.File.ReadAllBytes(CacheFilePath);
                        // Data is encrypted with DPAPI (user-specific)
                        var unprotected = System.Security.Cryptography.ProtectedData.Unprotect(
                            data, null, System.Security.Cryptography.DataProtectionScope.CurrentUser);
                        args.TokenCache.DeserializeMsalV3(unprotected);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Auth] Error loading token cache: {ex.Message}");
                }
            }
        }

        private static void AfterAccessNotification(TokenCacheNotificationArgs args)
        {
            if (args.HasStateChanged)
            {
                lock (FileLock)
                {
                    try
                    {
                        var dir = System.IO.Path.GetDirectoryName(CacheFilePath);
                        if (!System.IO.Directory.Exists(dir))
                        {
                            System.IO.Directory.CreateDirectory(dir);
                        }

                        // Encrypt with DPAPI before saving
                        var data = args.TokenCache.SerializeMsalV3();
                        var protectedData = System.Security.Cryptography.ProtectedData.Protect(
                            data, null, System.Security.Cryptography.DataProtectionScope.CurrentUser);
                        System.IO.File.WriteAllBytes(CacheFilePath, protectedData);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[Auth] Error saving token cache: {ex.Message}");
                    }
                }
            }
        }
    }
}
