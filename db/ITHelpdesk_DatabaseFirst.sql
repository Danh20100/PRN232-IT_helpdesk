-- IT Helpdesk - SQL Server Database First
-- Run in SQL Server Management Studio against your SQL Server instance.
IF DB_ID(N'ITHelpdeskDB') IS NULL
    CREATE DATABASE ITHelpdeskDB;
GO
USE ITHelpdeskDB;
GO

CREATE TABLE dbo.Users (
    UserId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Users PRIMARY KEY,
    FullName NVARCHAR(100) NOT NULL,
    Email NVARCHAR(255) NOT NULL,
    PasswordHash NVARCHAR(255) NOT NULL,
    PhoneNumber VARCHAR(20) NULL,
    AvatarUrl NVARCHAR(500) NULL,
    Role NVARCHAR(20) NOT NULL CONSTRAINT DF_Users_Role DEFAULT N'User',
    IsActive BIT NOT NULL CONSTRAINT DF_Users_IsActive DEFAULT 1,
    CreatedAt DATETIME2 NOT NULL CONSTRAINT DF_Users_CreatedAt DEFAULT SYSUTCDATETIME(),
    CONSTRAINT UQ_Users_Email UNIQUE (Email),
    CONSTRAINT CK_Users_Role CHECK (Role IN (N'User', N'Staff', N'Admin'))
);
GO
CREATE TABLE dbo.Categories (
    CategoryId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Categories PRIMARY KEY,
    CategoryName NVARCHAR(100) NOT NULL,
    Description NVARCHAR(500) NULL,
    IsActive BIT NOT NULL CONSTRAINT DF_Categories_IsActive DEFAULT 1,
    CONSTRAINT UQ_Categories_CategoryName UNIQUE (CategoryName)
);
GO
CREATE TABLE dbo.Tickets (
    TicketId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Tickets PRIMARY KEY,
    TicketCode VARCHAR(20) NOT NULL,
    Title NVARCHAR(200) NOT NULL,
    Description NVARCHAR(MAX) NOT NULL,
    CategoryId INT NOT NULL,
    CreatedBy INT NOT NULL,
    AssignedTo INT NULL,
    Priority TINYINT NOT NULL CONSTRAINT DF_Tickets_Priority DEFAULT 1,
    Status TINYINT NOT NULL CONSTRAINT DF_Tickets_Status DEFAULT 0,
    DueDate DATETIME2 NULL,
    CreatedAt DATETIME2 NOT NULL CONSTRAINT DF_Tickets_CreatedAt DEFAULT SYSUTCDATETIME(),
    UpdatedAt DATETIME2 NULL,
    ResolvedAt DATETIME2 NULL,
    ClosedAt DATETIME2 NULL,
    CONSTRAINT UQ_Tickets_TicketCode UNIQUE (TicketCode),
    CONSTRAINT CK_Tickets_Priority CHECK (Priority BETWEEN 0 AND 3),
    CONSTRAINT CK_Tickets_Status CHECK (Status BETWEEN 0 AND 5),
    CONSTRAINT FK_Tickets_Categories FOREIGN KEY (CategoryId) REFERENCES dbo.Categories(CategoryId) ON DELETE NO ACTION,
    CONSTRAINT FK_Tickets_CreatedBy FOREIGN KEY (CreatedBy) REFERENCES dbo.Users(UserId) ON DELETE NO ACTION,
    CONSTRAINT FK_Tickets_AssignedTo FOREIGN KEY (AssignedTo) REFERENCES dbo.Users(UserId) ON DELETE NO ACTION
);
GO
CREATE TABLE dbo.TicketComments (
    CommentId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_TicketComments PRIMARY KEY,
    TicketId INT NOT NULL,
    UserId INT NOT NULL,
    Content NVARCHAR(2000) NOT NULL,
    CreatedAt DATETIME2 NOT NULL CONSTRAINT DF_TicketComments_CreatedAt DEFAULT SYSUTCDATETIME(),
    CONSTRAINT CK_TicketComments_Content CHECK (LEN(LTRIM(RTRIM(Content))) > 0),
    CONSTRAINT FK_TicketComments_Tickets FOREIGN KEY (TicketId) REFERENCES dbo.Tickets(TicketId) ON DELETE NO ACTION,
    CONSTRAINT FK_TicketComments_Users FOREIGN KEY (UserId) REFERENCES dbo.Users(UserId) ON DELETE NO ACTION
);
GO
CREATE TABLE dbo.TicketAttachments (
    AttachmentId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_TicketAttachments PRIMARY KEY,
    TicketId INT NOT NULL,
    UploadedBy INT NOT NULL,
    FileName NVARCHAR(255) NOT NULL,
    FilePath NVARCHAR(500) NOT NULL,
    FileSize BIGINT NOT NULL,
    UploadedAt DATETIME2 NOT NULL CONSTRAINT DF_TicketAttachments_UploadedAt DEFAULT SYSUTCDATETIME(),
    CONSTRAINT CK_TicketAttachments_FileSize CHECK (FileSize >= 0),
    CONSTRAINT FK_TicketAttachments_Tickets FOREIGN KEY (TicketId) REFERENCES dbo.Tickets(TicketId) ON DELETE NO ACTION,
    CONSTRAINT FK_TicketAttachments_Users FOREIGN KEY (UploadedBy) REFERENCES dbo.Users(UserId) ON DELETE NO ACTION
);
GO
CREATE TABLE dbo.TicketStatusHistories (
    HistoryId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_TicketStatusHistories PRIMARY KEY,
    TicketId INT NOT NULL,
    ChangedBy INT NOT NULL,
    OldStatus TINYINT NOT NULL,
    NewStatus TINYINT NOT NULL,
    Note NVARCHAR(1000) NULL,
    ChangedAt DATETIME2 NOT NULL CONSTRAINT DF_TicketStatusHistories_ChangedAt DEFAULT SYSUTCDATETIME(),
    CONSTRAINT CK_TicketStatusHistories_OldStatus CHECK (OldStatus BETWEEN 0 AND 5),
    CONSTRAINT CK_TicketStatusHistories_NewStatus CHECK (NewStatus BETWEEN 0 AND 5),
    CONSTRAINT FK_TicketStatusHistories_Tickets FOREIGN KEY (TicketId) REFERENCES dbo.Tickets(TicketId) ON DELETE NO ACTION,
    CONSTRAINT FK_TicketStatusHistories_Users FOREIGN KEY (ChangedBy) REFERENCES dbo.Users(UserId) ON DELETE NO ACTION
);
GO
CREATE TABLE dbo.TicketAssignmentHistories (
    AssignmentId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_TicketAssignmentHistories PRIMARY KEY,
    TicketId INT NOT NULL,
    AssignedBy INT NOT NULL,
    OldAssigneeId INT NULL,
    NewAssigneeId INT NOT NULL,
    AssignedAt DATETIME2 NOT NULL CONSTRAINT DF_TicketAssignmentHistories_AssignedAt DEFAULT SYSUTCDATETIME(),
    CONSTRAINT FK_TicketAssignmentHistories_Tickets FOREIGN KEY (TicketId) REFERENCES dbo.Tickets(TicketId) ON DELETE NO ACTION,
    CONSTRAINT FK_TicketAssignmentHistories_AssignedBy FOREIGN KEY (AssignedBy) REFERENCES dbo.Users(UserId) ON DELETE NO ACTION,
    CONSTRAINT FK_TicketAssignmentHistories_OldAssignee FOREIGN KEY (OldAssigneeId) REFERENCES dbo.Users(UserId) ON DELETE NO ACTION,
    CONSTRAINT FK_TicketAssignmentHistories_NewAssignee FOREIGN KEY (NewAssigneeId) REFERENCES dbo.Users(UserId) ON DELETE NO ACTION
);
GO
CREATE TABLE dbo.TicketFeedbacks (
    FeedbackId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_TicketFeedbacks PRIMARY KEY,
    TicketId INT NOT NULL,
    UserId INT NOT NULL,
    Rating TINYINT NOT NULL,
    Comment NVARCHAR(1000) NULL,
    CreatedAt DATETIME2 NOT NULL CONSTRAINT DF_TicketFeedbacks_CreatedAt DEFAULT SYSUTCDATETIME(),
    CONSTRAINT UQ_TicketFeedbacks_TicketId UNIQUE (TicketId),
    CONSTRAINT CK_TicketFeedbacks_Rating CHECK (Rating BETWEEN 1 AND 5),
    CONSTRAINT FK_TicketFeedbacks_Tickets FOREIGN KEY (TicketId) REFERENCES dbo.Tickets(TicketId) ON DELETE NO ACTION,
    CONSTRAINT FK_TicketFeedbacks_Users FOREIGN KEY (UserId) REFERENCES dbo.Users(UserId) ON DELETE NO ACTION
);
GO
CREATE TABLE dbo.Notifications (
    NotificationId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Notifications PRIMARY KEY,
    UserId INT NOT NULL,
    TicketId INT NULL,
    Title NVARCHAR(200) NOT NULL,
    Message NVARCHAR(1000) NOT NULL,
    IsRead BIT NOT NULL CONSTRAINT DF_Notifications_IsRead DEFAULT 0,
    CreatedAt DATETIME2 NOT NULL CONSTRAINT DF_Notifications_CreatedAt DEFAULT SYSUTCDATETIME(),
    CONSTRAINT FK_Notifications_Users FOREIGN KEY (UserId) REFERENCES dbo.Users(UserId) ON DELETE NO ACTION,
    CONSTRAINT FK_Notifications_Tickets FOREIGN KEY (TicketId) REFERENCES dbo.Tickets(TicketId) ON DELETE NO ACTION
);
GO
CREATE INDEX IX_Tickets_Status ON dbo.Tickets(Status);
CREATE INDEX IX_Tickets_CreatedBy ON dbo.Tickets(CreatedBy);
CREATE INDEX IX_Tickets_AssignedTo ON dbo.Tickets(AssignedTo);
CREATE INDEX IX_Tickets_CategoryId ON dbo.Tickets(CategoryId);
CREATE INDEX IX_Tickets_DueDate ON dbo.Tickets(DueDate);
CREATE INDEX IX_TicketComments_TicketId_CreatedAt ON dbo.TicketComments(TicketId, CreatedAt);
CREATE INDEX IX_TicketAttachments_TicketId ON dbo.TicketAttachments(TicketId);
CREATE INDEX IX_TicketStatusHistories_TicketId_ChangedAt ON dbo.TicketStatusHistories(TicketId, ChangedAt);
CREATE INDEX IX_TicketAssignmentHistories_TicketId ON dbo.TicketAssignmentHistories(TicketId);
CREATE INDEX IX_Notifications_UserId_IsRead ON dbo.Notifications(UserId, IsRead);
GO
INSERT INTO dbo.Categories(CategoryName, Description) VALUES
(N'Hardware', N'Sự cố phần cứng'),
(N'Software', N'Sự cố phần mềm'),
(N'Network', N'Sự cố mạng'),
(N'Account', N'Sự cố tài khoản'),
(N'Printer', N'Sự cố máy in'),
(N'Other', N'Các sự cố khác');
GO
-- The backend must generate the unique TicketCode (for example, TCK-0001).
-- Do not seed plain text or fabricated password hashes into Users.
-- Role authorization, ticket workflow, and ownership checks must be enforced in the API/service layer.
SELECT name AS TableName FROM sys.tables ORDER BY name;
SELECT * FROM dbo.Categories ORDER BY CategoryId;
GO
