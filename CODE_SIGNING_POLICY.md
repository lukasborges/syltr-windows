# Code signing policy

Provisioning status: pending acceptance by SignPath Foundation. Once enabled,
official Windows releases will use: **Free code signing provided by SignPath.io, certificate by SignPath Foundation**.

Only artifacts built from this public repository by the project's GitHub
Actions release workflow may be submitted for signing. Locally built binaries,
pull-request artifacts and unsigned candidates are never published as official
releases. Every signing request requires manual approval before the signed
artifact can be published.

## Team roles

- Committers and reviewers: [Lucas Borges (`@lukasborges`)](https://github.com/lukasborges)
- Approvers: [Lucas Borges (`@lukasborges`)](https://github.com/lukasborges)

Contributions from people without commit access require review by a project
maintainer. Changes to build, packaging, signing or release workflows receive
the same review as application source code.

## Privacy

Syltr's data handling is described in the [privacy policy](PRIVACY.md). Syltr
does not transfer information to a project-operated service. Hosted web
services communicate with networks chosen by the user and remain subject to
their own privacy policies.

## Release verification

Public release notes will identify the source revision and include SHA-256
hashes for downloadable assets. Before publication, the release workflow must
verify that the MSIX has a valid Authenticode signature and that the
certificate subject exactly matches the package manifest publisher.
