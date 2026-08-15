/**
 * Identity 线上 DTO → 前端认证类型映射(§15 → §10.1 字段投影)。
 * 投影规则:userNId→userId、loginName→username、name→displayName、
 * tenantNId→tenantId、roleNIds→roles、permissionNIds→permissions、
 * mustChangePassword→mustChangePassword(§29A.4 首次登录改密门禁)。
 * 结构不符(字段缺失/类型错)一律抛 invalidResponse,与「解析失败视为无效」
 * 的会话策略一致,避免把脏数据带入 Store。
 */

import { createCorrelationId } from '@/api/correlation'
import { createApiError, DEFAULT_ERROR_MESSAGES } from '@/api/errors'
import type { AuthSession, AuthUser } from '@/auth/types'

import type { IdentityAuthSessionDto, IdentityAuthUserDto } from './types'

function isStringArray(value: unknown): value is string[] {
  return Array.isArray(value) && value.every((item) => typeof item === 'string')
}

function invalidResponse(): never {
  throw createApiError(
    'invalidResponse',
    DEFAULT_ERROR_MESSAGES.invalidResponse,
    createCorrelationId(),
  )
}

function isAuthUserDto(value: unknown): value is IdentityAuthUserDto {
  if (typeof value !== 'object' || value === null) return false
  const record = value as Record<string, unknown>
  return (
    typeof record['userNId'] === 'string' &&
    typeof record['loginName'] === 'string' &&
    typeof record['name'] === 'string' &&
    typeof record['tenantNId'] === 'string' &&
    isStringArray(record['roleNIds']) &&
    isStringArray(record['permissionNIds']) &&
    typeof record['mustChangePassword'] === 'boolean'
  )
}

function isAuthSessionDto(value: unknown): value is IdentityAuthSessionDto {
  if (typeof value !== 'object' || value === null) return false
  const record = value as Record<string, unknown>
  return (
    typeof record['accessToken'] === 'string' &&
    typeof record['refreshToken'] === 'string' &&
    typeof record['expiresAt'] === 'string' &&
    isAuthUserDto(record['user'])
  )
}

/** 线上 AuthUser → 前端 AuthUser;结构不符抛 invalidResponse。 */
export function mapAuthUser(dto: unknown): AuthUser {
  if (!isAuthUserDto(dto)) invalidResponse()
  return {
    userId: dto.userNId,
    username: dto.loginName,
    displayName: dto.name,
    tenantId: dto.tenantNId,
    roles: [...dto.roleNIds],
    permissions: [...dto.permissionNIds],
    mustChangePassword: dto.mustChangePassword,
  }
}

/** 线上 AuthSession → 前端 AuthSession;结构不符抛 invalidResponse。 */
export function mapAuthSession(dto: unknown): AuthSession {
  if (!isAuthSessionDto(dto)) invalidResponse()
  return {
    accessToken: dto.accessToken,
    refreshToken: dto.refreshToken,
    expiresAt: dto.expiresAt,
    user: mapAuthUser(dto.user),
  }
}
