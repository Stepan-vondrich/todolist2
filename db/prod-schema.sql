-- Prod schema updates for Azure SQL.
--
-- The app uses EF Core EnsureCreated() for the SqlServer provider: it builds the
-- schema on first run but does NOT apply later changes. So every column added to
-- the model after the prod DB already exists must be applied here. This script is
-- run by the CD pipeline's deploy-prod job (after manual approval, before the new
-- image is rolled out).
--
-- Keep every statement IDEMPOTENT (guarded by an existence check) so it is safe to
-- run on every prod deploy. Append new changes at the bottom; never edit/remove old
-- ones.

IF COL_LENGTH('dbo.CommentAttachments', 'FileName') IS NULL
    ALTER TABLE dbo.CommentAttachments ADD FileName nvarchar(max) NULL;

IF COL_LENGTH('dbo.CommentAttachments', 'PageTexts') IS NULL
    ALTER TABLE dbo.CommentAttachments ADD PageTexts nvarchar(max) NULL;
