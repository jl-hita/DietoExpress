export interface DirectoryProfile {
  username: string;
  slug: string;
  fullName: string;
  clinicName: string;
  city: string;
  province: string;
  clinicLogo: string;
  publicBio: string;
  specialties: string;
  onlineConsultations: boolean;
}


export interface PublicAvailabilitySlot {
  startsAt: string;
  endsAt: string;
  nutritionistId: number;
  nutritionistName?: string;
}
