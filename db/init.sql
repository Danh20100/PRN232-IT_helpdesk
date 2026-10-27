-- IT Helpdesk - MySQL 8.0 Initialization Script
-- Derived from ITHelpdesk_DatabaseFirst.sql for MySQL compatibility

CREATE DATABASE IF NOT EXISTS `ITHelpdeskDB` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
USE `ITHelpdeskDB`;

-- 1. Users Table
CREATE TABLE IF NOT EXISTS `Users` (
    `UserId` INT AUTO_INCREMENT NOT NULL,
    `FullName` VARCHAR(100) NOT NULL,
    `Email` VARCHAR(255) NOT NULL,
    `PasswordHash` VARCHAR(255) NOT NULL,
    `PhoneNumber` VARCHAR(20) NULL,
    `AvatarUrl` VARCHAR(500) NULL,
    `Role` VARCHAR(20) NOT NULL DEFAULT 'User',
    `IsActive` TINYINT(1) NOT NULL DEFAULT 1,
    `CreatedAt` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (`UserId`),
    UNIQUE KEY `UQ_Users_Email` (`Email`),
    CONSTRAINT `CK_Users_Role` CHECK (`Role` IN ('User', 'Staff', 'Admin'))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- 2. Categories Table
CREATE TABLE IF NOT EXISTS `Categories` (
    `CategoryId` INT AUTO_INCREMENT NOT NULL,
    `CategoryName` VARCHAR(100) NOT NULL,
    `Description` VARCHAR(500) NULL,
    `IsActive` TINYINT(1) NOT NULL DEFAULT 1,
    PRIMARY KEY (`CategoryId`),
    UNIQUE KEY `UQ_Categories_CategoryName` (`CategoryName`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- 3. Tickets Table
CREATE TABLE IF NOT EXISTS `Tickets` (
    `TicketId` INT AUTO_INCREMENT NOT NULL,
    `TicketCode` VARCHAR(20) NOT NULL,
    `Title` VARCHAR(200) NOT NULL,
    `Description` TEXT NOT NULL,
    `CategoryId` INT NOT NULL,
    `CreatedBy` INT NOT NULL,
    `AssignedTo` INT NULL,
    `Priority` TINYINT NOT NULL DEFAULT 1,
    `Status` TINYINT NOT NULL DEFAULT 0,
    `DueDate` DATETIME NULL,
    `CreatedAt` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    `UpdatedAt` DATETIME NULL,
    `ResolvedAt` DATETIME NULL,
    `ClosedAt` DATETIME NULL,
    PRIMARY KEY (`TicketId`),
    UNIQUE KEY `UQ_Tickets_TicketCode` (`TicketCode`),
    CONSTRAINT `CK_Tickets_Priority` CHECK (`Priority` BETWEEN 0 AND 3),
    CONSTRAINT `CK_Tickets_Status` CHECK (`Status` BETWEEN 0 AND 5),
    CONSTRAINT `FK_Tickets_Categories` FOREIGN KEY (`CategoryId`) REFERENCES `Categories` (`CategoryId`) ON DELETE RESTRICT,
    CONSTRAINT `FK_Tickets_CreatedBy` FOREIGN KEY (`CreatedBy`) REFERENCES `Users` (`UserId`) ON DELETE RESTRICT,
    CONSTRAINT `FK_Tickets_AssignedTo` FOREIGN KEY (`AssignedTo`) REFERENCES `Users` (`UserId`) ON DELETE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- 4. Ticket Comments Table
CREATE TABLE IF NOT EXISTS `TicketComments` (
    `CommentId` INT AUTO_INCREMENT NOT NULL,
    `TicketId` INT NOT NULL,
    `UserId` INT NOT NULL,
    `Content` VARCHAR(2000) NOT NULL,
    `CreatedAt` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (`CommentId`),
    CONSTRAINT `CK_TicketComments_Content` CHECK (CHAR_LENGTH(TRIM(`Content`)) > 0),
    CONSTRAINT `FK_TicketComments_Tickets` FOREIGN KEY (`TicketId`) REFERENCES `Tickets` (`TicketId`) ON DELETE RESTRICT,
    CONSTRAINT `FK_TicketComments_Users` FOREIGN KEY (`UserId`) REFERENCES `Users` (`UserId`) ON DELETE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- 5. Ticket Attachments Table
CREATE TABLE IF NOT EXISTS `TicketAttachments` (
    `AttachmentId` INT AUTO_INCREMENT NOT NULL,
    `TicketId` INT NOT NULL,
    `UploadedBy` INT NOT NULL,
    `FileName` VARCHAR(255) NOT NULL,
    `FilePath` VARCHAR(500) NOT NULL,
    `FileSize` BIGINT NOT NULL,
    `UploadedAt` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (`AttachmentId`),
    CONSTRAINT `CK_TicketAttachments_FileSize` CHECK (`FileSize` >= 0),
    CONSTRAINT `FK_TicketAttachments_Tickets` FOREIGN KEY (`TicketId`) REFERENCES `Tickets` (`TicketId`) ON DELETE RESTRICT,
    CONSTRAINT `FK_TicketAttachments_Users` FOREIGN KEY (`UploadedBy`) REFERENCES `Users` (`UserId`) ON DELETE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- 6. Ticket Status Histories Table
CREATE TABLE IF NOT EXISTS `TicketStatusHistories` (
    `HistoryId` INT AUTO_INCREMENT NOT NULL,
    `TicketId` INT NOT NULL,
    `ChangedBy` INT NOT NULL,
    `OldStatus` TINYINT NOT NULL,
    `NewStatus` TINYINT NOT NULL,
    `Note` VARCHAR(1000) NULL,
    `ChangedAt` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (`HistoryId`),
    CONSTRAINT `CK_TicketStatusHistories_OldStatus` CHECK (`OldStatus` BETWEEN 0 AND 5),
    CONSTRAINT `CK_TicketStatusHistories_NewStatus` CHECK (`NewStatus` BETWEEN 0 AND 5),
    CONSTRAINT `FK_TicketStatusHistories_Tickets` FOREIGN KEY (`TicketId`) REFERENCES `Tickets` (`TicketId`) ON DELETE RESTRICT,
    CONSTRAINT `FK_TicketStatusHistories_Users` FOREIGN KEY (`ChangedBy`) REFERENCES `Users` (`UserId`) ON DELETE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- 7. Ticket Assignment Histories Table
CREATE TABLE IF NOT EXISTS `TicketAssignmentHistories` (
    `AssignmentId` INT AUTO_INCREMENT NOT NULL,
    `TicketId` INT NOT NULL,
    `AssignedBy` INT NOT NULL,
    `OldAssigneeId` INT NULL,
    `NewAssigneeId` INT NOT NULL,
    `AssignedAt` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (`AssignmentId`),
    CONSTRAINT `FK_TicketAssignmentHistories_Tickets` FOREIGN KEY (`TicketId`) REFERENCES `Tickets` (`TicketId`) ON DELETE RESTRICT,
    CONSTRAINT `FK_TicketAssignmentHistories_AssignedBy` FOREIGN KEY (`AssignedBy`) REFERENCES `Users` (`UserId`) ON DELETE RESTRICT,
    CONSTRAINT `FK_TicketAssignmentHistories_OldAssignee` FOREIGN KEY (`OldAssigneeId`) REFERENCES `Users` (`UserId`) ON DELETE RESTRICT,
    CONSTRAINT `FK_TicketAssignmentHistories_NewAssignee` FOREIGN KEY (`NewAssigneeId`) REFERENCES `Users` (`UserId`) ON DELETE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- 8. Ticket Feedbacks Table
CREATE TABLE IF NOT EXISTS `TicketFeedbacks` (
    `FeedbackId` INT AUTO_INCREMENT NOT NULL,
    `TicketId` INT NOT NULL,
    `UserId` INT NOT NULL,
    `Rating` TINYINT NOT NULL,
    `Comment` VARCHAR(1000) NULL,
    `CreatedAt` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (`FeedbackId`),
    UNIQUE KEY `UQ_TicketFeedbacks_TicketId` (`TicketId`),
    CONSTRAINT `CK_TicketFeedbacks_Rating` CHECK (`Rating` BETWEEN 1 AND 5),
    CONSTRAINT `FK_TicketFeedbacks_Tickets` FOREIGN KEY (`TicketId`) REFERENCES `Tickets` (`TicketId`) ON DELETE RESTRICT,
    CONSTRAINT `FK_TicketFeedbacks_Users` FOREIGN KEY (`UserId`) REFERENCES `Users` (`UserId`) ON DELETE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- 9. Notifications Table
CREATE TABLE IF NOT EXISTS `Notifications` (
    `NotificationId` INT AUTO_INCREMENT NOT NULL,
    `UserId` INT NOT NULL,
    `TicketId` INT NULL,
    `Title` VARCHAR(200) NOT NULL,
    `Message` VARCHAR(1000) NOT NULL,
    `IsRead` TINYINT(1) NOT NULL DEFAULT 0,
    `CreatedAt` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (`NotificationId`),
    CONSTRAINT `FK_Notifications_Users` FOREIGN KEY (`UserId`) REFERENCES `Users` (`UserId`) ON DELETE RESTRICT,
    CONSTRAINT `FK_Notifications_Tickets` FOREIGN KEY (`TicketId`) REFERENCES `Tickets` (`TicketId`) ON DELETE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Indexes for performance
CREATE INDEX `IX_Tickets_Status` ON `Tickets` (`Status`);
CREATE INDEX `IX_Tickets_CreatedBy` ON `Tickets` (`CreatedBy`);
CREATE INDEX `IX_Tickets_AssignedTo` ON `Tickets` (`AssignedTo`);
CREATE INDEX `IX_Tickets_CategoryId` ON `Tickets` (`CategoryId`);
CREATE INDEX `IX_Tickets_DueDate` ON `Tickets` (`DueDate`);
CREATE INDEX `IX_TicketComments_TicketId_CreatedAt` ON `TicketComments` (`TicketId`, `CreatedAt`);
CREATE INDEX `IX_TicketAttachments_TicketId` ON `TicketAttachments` (`TicketId`);
CREATE INDEX `IX_TicketStatusHistories_TicketId_ChangedAt` ON `TicketStatusHistories` (`TicketId`, `ChangedAt`);
CREATE INDEX `IX_TicketAssignmentHistories_TicketId` ON `TicketAssignmentHistories` (`TicketId`);
CREATE INDEX `IX_Notifications_UserId_IsRead` ON `Notifications` (`UserId`, `IsRead`);

-- Seed Default Categories
INSERT INTO `Categories` (`CategoryName`, `Description`) VALUES
('Hardware', 'Sự cố phần cứng'),
('Software', 'Sự cố phần mềm'),
('Network', 'Sự cố mạng'),
('Account', 'Sự cố tài khoản'),
('Printer', 'Sự cố máy in'),
('Other', 'Các sự cố khác')
ON DUPLICATE KEY UPDATE `CategoryName`=`CategoryName`;
