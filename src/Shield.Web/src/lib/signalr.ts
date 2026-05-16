import { HubConnectionBuilder, type HubConnection } from '@microsoft/signalr'

let connection: HubConnection | null = null

export function getFindingsConnection(): HubConnection {
  if (connection) return connection
  connection = new HubConnectionBuilder()
    .withUrl('/hubs/findings', { withCredentials: true })
    .withAutomaticReconnect()
    .build()
  return connection
}
