# Profile Picture Development Plan

## Approved design

Profile pictures are stored as files on the API host. The database stores only a generated public
path in the `Users` table. The API serves pictures from `/uploads/profile-pictures/`; the UI uses a
local default avatar whenever a user has no custom picture.

## Implementation

1. Add an optional `ProfilePicturePath` to the user entity, EF mapping, API/UI user contracts, and
   mappings. Add a migration so startup migration creates the column in existing databases.
2. Add configurable picture storage, size, dimensions, and allowed types. The API exposes only the
   configured picture directory as static content.
3. Add authenticated multipart upload/replacement endpoints for administrators/managers editing
   users and for users editing their own profiles. Validate decoded image content and size, re-encode
   to a bounded safe format, use generated filenames, audit changes, and delete superseded files.
4. Add multipart API-client support and picture upload controls/previews in the admin create/edit
   modals and self-service profile page. Keep JSON user creation/update endpoints unchanged; upload
   an optional picture after the user operation succeeds.
5. Add a default avatar in the UI. Display the image on the profile page and in the title bar beside
   the signed-in username. Renew the cookie after a user changes their own picture so the title bar
   updates immediately.
6. Add focused upload/mapping tests and document the multipart endpoints and constraints in
   `docs/api.md`.
