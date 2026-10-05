import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environments';
import { AddressSuggestion } from '../modelos/address';

@Injectable({ providedIn: 'root' })
/** Encapsula el acceso al backend para que los formularios no conozcan el proveedor geográfico externo. */
export class AddressService {
  private readonly base = environment.apiUrl;

  constructor(private readonly http: HttpClient) {}

  search(text: string): Observable<{ suggestions: AddressSuggestion[] }> {
    const params = new HttpParams().set('text', text.trim());
    return this.http.get<{ suggestions: AddressSuggestion[] }>(
      this.base + '/address-autocomplete',
      { params }
    );
  }
}
