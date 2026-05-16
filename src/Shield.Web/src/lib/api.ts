import axios from 'axios'

import { router } from '@/router'

export const api = axios.create({
  baseURL: '/api',
  withCredentials: true,
  headers: {
    'Content-Type': 'application/json',
  },
})

api.interceptors.response.use(
  response => response,
  (error) => {
    const status = error?.response?.status
    if (status === 401) {
      const currentPath = router.currentRoute.value.fullPath
      if (router.currentRoute.value.name !== 'login') {
        router.push({ name: 'login', query: { redirect: currentPath } })
      }
    }
    return Promise.reject(error)
  },
)
