-- A post with a publish time (Post) and an article without one (Article), for ContentTables=Post,Article
CREATE TABLE Post (
    PostId INTEGER PRIMARY KEY AUTOINCREMENT,
    Title VARCHAR(150) NOT NULL,
    Slug VARCHAR(150) NOT NULL,
    Body TEXT NOT NULL,
    Status VARCHAR(12) NOT NULL DEFAULT 'draft',
    PublishedOn DATETIME NULL,
    CreatedOn DATETIME NULL DEFAULT CURRENT_TIMESTAMP
);
CREATE UNIQUE INDEX UX_Post_Slug ON Post (Slug);

CREATE TABLE Article (
    ArticleId INTEGER PRIMARY KEY AUTOINCREMENT,
    Title VARCHAR(100) NOT NULL,
    Slug VARCHAR(40) NOT NULL,
    Status VARCHAR(10) NOT NULL DEFAULT 'draft',
    Body TEXT NULL,
    Summary VARCHAR(200) NULL
);
CREATE UNIQUE INDEX UX_Article_Slug ON Article (Slug);

CREATE TABLE PostComment (
    PostCommentId INTEGER PRIMARY KEY AUTOINCREMENT,
    PostId INTEGER NOT NULL REFERENCES Post (PostId),
    Author VARCHAR(50) NOT NULL,
    Remark VARCHAR(200) NOT NULL,
    Rating INTEGER NOT NULL DEFAULT 0,
    PostedOn DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP
);

INSERT INTO Post (Title, Slug, Body, Status, PublishedOn) VALUES ('Welcome', 'welcome', 'First post', 'published', '2026-01-01 09:00:00');
INSERT INTO Post (Title, Slug, Body, Status) VALUES ('Coming soon', 'coming-soon', 'A draft', 'draft');
INSERT INTO PostComment (PostId, Author, Remark) VALUES (1, 'Pat', 'Nice start');
