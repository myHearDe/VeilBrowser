# Copy this file to signing.local.ps1 and edit ONE signing identity.
# signing.local.ps1 is ignored by Git and must never be committed.

@{
    # Option A: PFX certificate. Keep the PFX outside this repository.
    PfxPath = "D:\Certificates\my-code-signing-certificate.pfx"

    # Option B: certificate already installed in the Windows certificate store.
    # Leave PfxPath empty when using a thumbprint.
    CertificateThumbprint = ""
    CertificateStoreLocation = "CurrentUser"

    # RFC 3161 timestamp server. Change this if your CA specifies another URL.
    TimestampUrl = "http://timestamp.digicert.com"
}
