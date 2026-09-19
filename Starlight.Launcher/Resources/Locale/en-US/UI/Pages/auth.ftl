# Errors/Warnings

auth-menu-token-verify-warning = The token could not be verified: { $ex }
auth-menu-token-verify-offline = Could not reach the auth server. Using the saved session.
auth-menu-session-expired-warning = Your session has expired. Please log in again.
auth-menu-discord-login-error = Discord login canceled or expired.
auth-menu-discord-connect-fail = Failed to connect Discord.
auth-menu-enter-info-error = Enter your username and password.
auth-menu-incorrect-info-error = Incorrect username or password.
auth-menu-unconfirmed-info-error = Your account has not been verified. Please check your email.
auth-menu-tfa-required-error = Enter your two-factor authentication code.
auth-menu-account-blocked-error = Account blocked.
auth-menu-forgot-notvalid-email-error = Enter valid email.

auth-menu-register-username-missing = Enter your username
auth-menu-register-invalid-email = Please enter a valid email address
auth-menu-register-too-short-pass = The password must be at least 8 characters long
auth-menu-register-dont-match-pass = The passwords do not match

# Snackbars(not including errors/warnings)

auth-menu-account-deleted = Account { $account } deleted.
auth-menu-discord-linked-status = Discord linked to { $account }.
auth-menu-steam-linked-status = Steam linked to { $account }.
auth-menu-discord-renewed = Discord session renewed.
auth-menu-welcome-message = Welcome, { $username }!
auth-menu-email-resent = The email has been resent.
auth-menu-email-resent-info = To resend, enter your email address in the username field.
auth-menu-register-success = Registration was successful! You can now log in.
auth-menu-register-required-confirmation = Registration was successful! An email has been sent to { $email } to confirm your account.

# Statuses

auth-menu-online-status = Online
auth-menu-expired-status = The session has expired
auth-menu-unsure-status = Checking...
auth-menu-unreachable-status = Could not reach the auth server

# Account List

auth-menu-accounts-mode = Accounts
auth-menu-active-chip = Active
auth-menu-no-accounts = You haven't added any accounts yet.
auth-menu-no-accounts-hint = Press "Add Account" to login or register in system
auth-menu-discord-linked-tooltip = Discord linked
auth-menu-steam-linked-tooltip = Steam linked
auth-menu-login-again-button = Login again
auth-menu-select-button = Select
auth-menu-link-discord-button = Link Discord
auth-menu-add-account-button = Add Account
auth-menu-delete-account-button = Delete Account

# SignIn

auth-menu-signin-login-again = Log in again
auth-menu-signin-login = Log in
auth-menu-signin-status-busy = Login...
auth-menu-signin-status-wait = Sign In

# General Entries

auth-menu-username-label = Username
auth-menu-email-label = Email
auth-menu-password-label = Password
auth-menu-confirm-label = Confirm Password
auth-menu-2fa-label = 2FA Code
auth-menu-resend-confirmation = Resend the confirmation email
auth-menu-signin-method-discord = Login with Discord
auth-menu-signin-method-steam = Login with Steam
auth-menu-signin-forgot-password = Forgot your password?
auth-menu-signin = Sign In

# Registration

auth-menu-registration = Registration
auth-menu-registration-status-busy = Registration...
auth-menu-registration-status-wait = Sign up

auth-menu-registration-disabled-title = Registration disabled
auth-menu-registration-disabled-hint = 
    At this time, the official developer has denied the request for a restoration of the registration API.
    Please use Discord to register or visit the official SS14 website.

auth-menu-registration-back-to-signin = Back to Sign In

# ForgotPassword

auth-menu-forgotpassword = Password Recovery
auth-menu-forgotpassword-alert = If an account with that email address exists, password reset instructions have been sent to it.
auth-menu-enter-email-label = Enter the email address associated with your account.
auth-menu-forgotpassword-status-busy = Sending...
auth-menu-forgotpassword-status-wait = Send

# Link

auth-menu-can-play-no-discord = Can play without Discord
auth-menu-link-account-button = Link account
auth-menu-needs-discord = Requires auth via Discord
auth-menu-link-account-title = Account Linking
auth-menu-link-account-hint     = Log in to your authentication server account to play without Discord
auth-menu-link-account-confirm  = Link
auth-menu-link-account-busy     = Linking...
auth-menu-account-linked        = The { $account } account was linked

# NullLink profile

auth-menu-link-steam-button = Link Steam
auth-profile-make-active = Play on this account
auth-profile-refresh = Refresh
auth-profile-user-id = Account ID
auth-profile-total-playtime = Total playtime
auth-profile-servers-played = Servers
auth-profile-achievements-count = Achievements
auth-profile-top-role = Top role
auth-profile-duration-m = { $minutes }m
auth-profile-duration-hm = { $hours }h { $minutes }m
auth-profile-playtime-title = Playtime by server
auth-profile-playtime-empty = NullLink has no playtime recorded for this account yet.
auth-profile-server-offline = Server is offline or no longer listed
auth-profile-more-roles = +{ $count } more
auth-profile-less-roles = Show less
auth-profile-achievements-title = Achievements
auth-profile-achievements-empty = No achievements unlocked yet.
auth-profile-achievement-by = { $character } on { $server }
auth-profile-achievement-server = On { $server }
auth-profile-resources-title = Resources
auth-profile-not-linked-title = NullLink profile is not available
auth-profile-not-linked-hint = NullLink recognizes players by their Starlight session. Link Discord or Steam to this account to see its playtime and achievements.
auth-profile-unauthorized-title = Session expired
auth-profile-unauthorized-hint = Log in again to load the NullLink profile.
auth-profile-unavailable-title = Could not load the profile
auth-profile-unavailable-hint = NullLink is unreachable right now. Try again in a moment.
auth-profile-retry = Try again
