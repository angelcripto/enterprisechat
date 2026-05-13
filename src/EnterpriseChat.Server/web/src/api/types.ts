// Wire DTOs that mirror EnterpriseChat.Protocol. ASP.NET Core serializes
// records with camelCase keys by default, so these names match the JSON
// the server sends — not the C# property names.

export type UserRole = "Admin" | "User";

export interface LoginRequest {
    username: string;
    password: string;
}

export interface LoginResponse {
    accessToken: string;
    expiresAt: string;
    userId: number;
    username: string;
    fullName: string;
    role: UserRole;
}

export interface ChatMessage {
    messageId: string;
    serverId?: number | null;
    fromUserId: number;
    toUserId?: number | null;
    roomId?: number | null;
    body: string;
    sentAt: string;
    attachmentId?: number | null;
    attachmentFileName?: string | null;
    attachmentSizeBytes?: number | null;
}

export interface UserSummary {
    id: number;
    username: string;
    fullName: string;
    department?: string | null;
    role: UserRole;
    isOnline: boolean;
    hasAvatar: boolean;
    unreadDirectMessages: number;
}

export interface RoomSummary {
    id: number;
    name: string;
    isPrivate: boolean;
    createdByUserId: number;
    createdAt: string;
    isMember: boolean;
    memberCount: number;
}

export interface CreateRoomRequest {
    name: string;
    isPrivate: boolean;
}

export interface AttachmentSummary {
    id: number;
    fileName: string;
    mimeType: string;
    sizeBytes: number;
    uploadedByUserId: number;
    uploadedAt: string;
}

export interface SearchHit {
    serverId: number;
    fromUserId: number;
    fromUsername: string;
    toUserId?: number | null;
    roomId?: number | null;
    roomName?: string | null;
    body: string;
    sentAt: string;
}

export interface SearchResponse {
    query: string;
    hits: SearchHit[];
}

export interface LicenseInfo {
    /** Server serializes the enum as int by default (0 = Free, 1 = Pro). The
     *  string form is what the marketing site uses. We accept both. */
    edition: "Free" | "Pro" | 0 | 1;
    maxConcurrentUsers: number;
    expiresAt?: string | null;
    licensedTo?: string | null;
    licenseId?: string | null;
}

export function isProEdition(info: LicenseInfo | null | undefined): boolean {
    if (info === null || info === undefined) return false;
    return info.edition === "Pro" || info.edition === 1;
}

export interface PinnedSummary {
    roomId: number;
    messageId: number;
    pinnedByUserId: number;
    pinnedAt: string;
    authorUserId: number;
    body: string;
    sentAt: string;
}

export interface ReactionSummary {
    messageId: number;
    emoji: string;
    count: number;
    mine: boolean;
}

export interface ServerMetrics {
    activeUsers: number;
    maxUsers: number;
    storageUsedBytes: number;
    storageQuotaBytes: number;
    messageCount: number;
    roomCount: number;
}

export interface ApplyLicenseRequest {
    serial: string;
}

export interface ApplyLicenseResponse {
    success: boolean;
    errorMessage?: string | null;
}

/** Direction filter for the message store. A "thread" is either a 1-on-1 DM
 * with a user or a chat room. */
export type ThreadKey =
    | { kind: "dm"; peerUserId: number }
    | { kind: "room"; roomId: number };

export function threadKeyId(k: ThreadKey): string {
    return k.kind === "dm" ? `dm:${k.peerUserId}` : `room:${k.roomId}`;
}
