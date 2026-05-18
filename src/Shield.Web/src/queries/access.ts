import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'

import { api } from '@/lib/api'
import type {
  AccessUser,
  AcceptInviteRequest,
  AcceptInviteResponse,
  AddGroupMemberRequest,
  CreateGroupRequest,
  GithubMemberListResponse,
  GithubOrgListResponse,
  GithubUserSearchResponse,
  GrantSourceRequest,
  InviteUserRequest,
  InviteUserResponse,
  PendingInvite,
  PublicInvitePreview,
  SourceGrant,
  SourceGrants,
  SourceGroup,
  UpdateGroupRequest,
} from '@/types/api'

export const useAccessUsersQuery = () => useQuery({
  queryKey: ['access', 'users'],
  queryFn: async (): Promise<AccessUser[]> => {
    const { data } = await api.get<AccessUser[]>('/access/users')
    return data
  },
})

export const useAccessGroupsQuery = () => useQuery({
  queryKey: ['access', 'groups'],
  queryFn: async (): Promise<SourceGroup[]> => {
    const { data } = await api.get<SourceGroup[]>('/access/groups')
    return data
  },
})

export const useSourceGrantsQuery = (sourceId: number) => useQuery({
  queryKey: ['access', 'sources', sourceId, 'grants'],
  queryFn: async (): Promise<SourceGrants> => {
    const { data } = await api.get<SourceGrants>(`/access/sources/${sourceId}`)
    return data
  },
})

export const useCreateGroupMutation = () => {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (payload: CreateGroupRequest): Promise<SourceGroup> => {
      const { data } = await api.post<SourceGroup>('/access/groups', payload)
      return data
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['access', 'groups'] })
    },
  })
}

export const useUpdateGroupMutation = () => {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (input: { id: number, patch: UpdateGroupRequest }): Promise<SourceGroup> => {
      const { data } = await api.put<SourceGroup>(`/access/groups/${input.id}`, input.patch)
      return data
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['access', 'groups'] })
    },
  })
}

export const useDeleteGroupMutation = () => {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (id: number): Promise<void> => {
      await api.delete(`/access/groups/${id}`)
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['access', 'groups'] })
    },
  })
}

export const useAddGroupMemberMutation = () => {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (input: { groupId: number, member: AddGroupMemberRequest }) => {
      const { data } = await api.post(`/access/groups/${input.groupId}/members`, input.member)
      return data
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['access', 'groups'] })
    },
  })
}

export const useRemoveGroupMemberMutation = () => {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (input: { groupId: number, userId: string }): Promise<void> => {
      await api.delete(`/access/groups/${input.groupId}/members/${input.userId}`)
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['access', 'groups'] })
    },
  })
}

export const useGrantSourceMutation = () => {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (input: { sourceId: number, grant: GrantSourceRequest }): Promise<SourceGrant> => {
      const { data } = await api.post<SourceGrant>(`/access/sources/${input.sourceId}/grant`, input.grant)
      return data
    },
    onSuccess: (_data, input) => {
      queryClient.invalidateQueries({ queryKey: ['access', 'sources', input.sourceId, 'grants'] })
    },
  })
}

export const useRevokeSourceGrantMutation = () => {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (input: { sourceId: number, grantId: number }): Promise<void> => {
      await api.delete(`/access/sources/${input.sourceId}/grant/${input.grantId}`)
    },
    onSuccess: (_data, input) => {
      queryClient.invalidateQueries({ queryKey: ['access', 'sources', input.sourceId, 'grants'] })
    },
  })
}

export const useInviteUserMutation = () => {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (payload: InviteUserRequest): Promise<InviteUserResponse> => {
      const { data } = await api.post<InviteUserResponse>('/access/invite', payload)
      return data
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['access', 'users'] })
      queryClient.invalidateQueries({ queryKey: ['access', 'invites'] })
    },
  })
}

export const usePendingInvitesQuery = () => useQuery({
  queryKey: ['access', 'invites'],
  queryFn: async (): Promise<PendingInvite[]> => {
    const { data } = await api.get<PendingInvite[]>('/access/invites')
    return data
  },
})

export const useResendInviteMutation = () => {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (inviteId: string): Promise<InviteUserResponse> => {
      const { data } = await api.post<InviteUserResponse>(`/access/invite/${inviteId}/resend`)
      return data
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['access', 'invites'] })
    },
  })
}

export const useRevokeInviteMutation = () => {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (inviteId: string): Promise<void> => {
      await api.delete(`/access/invite/${inviteId}`)
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['access', 'invites'] })
    },
  })
}

// ---------- GitHub collaborator picker ----------

// Accept a getter so the caller can pass a reactive ref/computed and have TanStack
// re-evaluate enabled on every dependency change. Passing a static boolean captured the
// value at construction time and the query never re-enabled when the invite dialog
// opened — refetch() can't rescue it either, it's a no-op on a disabled query in v5.
export const useGithubOrgsQuery = (enabled: boolean | (() => boolean) = true) => useQuery({
  queryKey: ['collaborators', 'github', 'orgs'],
  enabled,
  queryFn: async (): Promise<GithubOrgListResponse> => {
    const { data } = await api.get<GithubOrgListResponse>('/collaborators/github/orgs')
    return data
  },
})

export const useGithubOrgMembersQuery = (org: () => string | null, page: () => number, perPage = 100) => useQuery({
  queryKey: ['collaborators', 'github', 'orgs', org, 'members', page, perPage],
  enabled: () => !!org() && org()!.length > 0,
  queryFn: async (): Promise<GithubMemberListResponse> => {
    const { data } = await api.get<GithubMemberListResponse>(
      `/collaborators/github/orgs/${encodeURIComponent(org()!)}/members?page=${page()}&perPage=${perPage}`,
    )
    return data
  },
})

export async function searchGithubUsers(query: string): Promise<GithubUserSearchResponse> {
  const { data } = await api.get<GithubUserSearchResponse>(
    `/collaborators/github/users/search?q=${encodeURIComponent(query)}`,
  )
  return data
}

// ---------- GitHub-derived access mirror ----------

export interface RefreshGithubAccessResponse {
  userId: string
  sourceCount: number
  orgs: string[]
  hasGithubLogin: boolean
}

// Triggers a server-side re-pull of the user's GitHub org map. Returns the count of sources
// granted via this layer + the list of orgs that backed those grants (used in the toast so
// the user can see WHY they got access).
export const useRefreshGithubAccessMutation = () => {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (userId?: string): Promise<RefreshGithubAccessResponse> => {
      const url = userId
        ? `/access/refresh-github-permissions?userId=${encodeURIComponent(userId)}`
        : '/access/refresh-github-permissions'
      const { data } = await api.post<RefreshGithubAccessResponse>(url)
      return data
    },
    onSuccess: () => {
      // Sources list visibility changes the instant the snapshot updates.
      queryClient.invalidateQueries({ queryKey: ['sources'] })
    },
  })
}

// Public — anonymous fetch the accept-invite page hits before the user signs in.
export async function fetchInvitePreview(token: string): Promise<PublicInvitePreview> {
  const { data } = await api.get<PublicInvitePreview>(`/access/invite/${encodeURIComponent(token)}`)
  return data
}

export async function acceptInvite(payload: AcceptInviteRequest): Promise<AcceptInviteResponse> {
  const { data } = await api.post<AcceptInviteResponse>('/auth/accept-invite', payload)
  return data
}
