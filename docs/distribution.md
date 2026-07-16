# GitHub distribution

Syltr will use GitHub Releases as its first public distribution and update
channel. The package identity GUID must remain stable across releases so that
Windows treats later packages as updates rather than unrelated applications.

## Release assets

Every public x64 release will expose these stable asset names:

- `Syltr-x64.msix`: the trusted, signed application package;
- `Syltr.appinstaller`: the installer and update descriptor.

The App Installer descriptor points to GitHub's `releases/latest/download`
URLs and checks for an update when Syltr is launched. Users can download and
open `Syltr.appinstaller` directly. The `ms-appinstaller:` URI protocol is not
part of this design because it is disabled by default on current Windows
versions.

Run `scripts/new-appinstaller.ps1` against the final MSIX to generate the
descriptor. Its identity, publisher, version and architecture are read from
the package manifest to prevent the two files from diverging.

## Signing gate

The current `CN=AppPublisher` value is provisional. A public package must never
be uploaded until it is signed by a certificate trusted on the target machine,
and the certificate subject must exactly match the `Publisher` in
`Package.appxmanifest`.

For this open-source project, the preferred path is an application to SignPath
Foundation. If the project is accepted, SignPath becomes the displayed package
publisher and its exact certificate subject must replace the provisional
publisher before signing. A paid organization-validation code-signing
certificate is the fallback when retaining a publisher controlled by the
project is more important than the cost.

The manually triggered `release-candidate.yml` workflow deliberately creates
only an unsigned GitHub Actions artifact. It has read-only repository
permissions and cannot create a public GitHub Release. After a signing provider
is selected, add signing and signature verification between package creation
and release publication.

## Production release checklist

1. Confirm that the application version and MSIX manifest version match.
2. Build and test the Release configuration.
3. Produce the x64 MSIX.
4. Sign it with the approved trusted certificate.
5. Verify the signature and that its subject matches the manifest publisher.
6. Generate `Syltr.appinstaller` from the signed package.
7. Test clean installation and upgrade from the preceding version.
8. Publish both stable asset names in the same GitHub Release.
