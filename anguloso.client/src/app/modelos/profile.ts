export interface Profile {
  username: string;
  email: string;
  fullName: string;
  clinicName?: string;
  clinicAddress?: string;
  clinicPhone?: string;
  clinicLogo?: string; // base64 string
}

export interface UpdateProfile {
  fullName: string;
  clinicName?: string;
  clinicAddress?: string;
  clinicPhone?: string;
  clinicLogo?: string; // base64 string
}
