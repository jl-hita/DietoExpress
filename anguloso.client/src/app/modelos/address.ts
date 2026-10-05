/** Resultado normalizado del autocompletado; no expone el contrato específico de Geoapify al resto de Angular. */
export interface AddressSuggestion {
  displayName: string;
  street: string;
  houseNumber: string;
  postalCode: string;
  city: string;
  province: string;
  country: string;
  countryCode: string;
  latitude?: number | null;
  longitude?: number | null;
}
