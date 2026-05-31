import { Injectable, InjectionToken, inject } from '@angular/core';
import { Subject, Observable } from 'rxjs';
import * as signalR from '@microsoft/signalr';
import { ActivityLog, OrderUpdate, GraphRun, NodeStreamChunk } from '../models';

export const SIGNALR_CONNECTION_FACTORY = new InjectionToken<() => signalR.HubConnection>(
  'SignalrConnectionFactory',
  {
    factory: () => () =>
      new signalR.HubConnectionBuilder()
        .withUrl('/hubs/activity')
        .withAutomaticReconnect()
        .build(),
  },
);

@Injectable({ providedIn: 'root' })
export class SignalrService {
  private connectionFactory = inject(SIGNALR_CONNECTION_FACTORY);
  private hubConnection?: signalR.HubConnection;
  private connectionPromise?: Promise<void>;
  private activitySubject = new Subject<ActivityLog>();
  private orderUpdateSubject = new Subject<OrderUpdate>();
  private graphRunSubject = new Subject<GraphRun>();
  private nodeStreamSubject = new Subject<NodeStreamChunk>();

  /**
   * Pack IDs the client has subscribed to. SignalR groups are keyed by the
   * server-side connection ID, so when `withAutomaticReconnect` rebuilds the
   * underlying transport the client gets a fresh connection ID and loses its
   * group membership. We replay these on `onreconnected` so live events (node
   * streams, graph-run updates, activity logs) continue to flow without
   * requiring a manual page refresh.
   */
  private subscribedPacks = new Set<string>();

  onActivity$: Observable<ActivityLog> = this.activitySubject.asObservable();
  onOrderUpdate$: Observable<OrderUpdate> = this.orderUpdateSubject.asObservable();
  onGraphRunUpdate$: Observable<GraphRun> = this.graphRunSubject.asObservable();
  onNodeStream$: Observable<NodeStreamChunk> = this.nodeStreamSubject.asObservable();

  connect(): void {
    if (this.connectionPromise) return;

    this.hubConnection = this.connectionFactory();

    this.hubConnection.on('OnActivity', (activity: ActivityLog) => {
      this.activitySubject.next(activity);
    });

    this.hubConnection.on('OnOrderUpdate', (update: OrderUpdate) => {
      this.orderUpdateSubject.next(update);
    });

    this.hubConnection.on('OnGraphRunUpdate', (run: GraphRun) => {
      this.graphRunSubject.next(run);
    });

    this.hubConnection.on('OnNodeStream', (chunk: NodeStreamChunk) => {
      this.nodeStreamSubject.next(chunk);
    });

    // After an automatic reconnect the server treats us as a brand-new
    // connection, so any previous SubscribeToPack invocations are gone.
    // Replay every active subscription so the dashboard keeps streaming.
    this.hubConnection.onreconnected(() => {
      for (const packId of this.subscribedPacks) {
        this.hubConnection?.invoke('SubscribeToPack', packId)
          .catch(err => console.error('SignalR re-subscribe failed:', err));
      }
    });

    this.connectionPromise = this.hubConnection.start().catch(err => console.error('SignalR connection error:', err));
  }

  disconnect(): void {
    this.hubConnection?.stop();
    this.connectionPromise = undefined;
    this.subscribedPacks.clear();
  }

  subscribeToPack(packId: string): void {
    this.subscribedPacks.add(packId);
    this.connectionPromise?.then(() => this.hubConnection?.invoke('SubscribeToPack', packId));
  }

  unsubscribeFromPack(packId: string): void {
    this.subscribedPacks.delete(packId);
    this.connectionPromise?.then(() => this.hubConnection?.invoke('UnsubscribeFromPack', packId));
  }
}
