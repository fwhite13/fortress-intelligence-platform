export interface BrandContext {
    orgId: string;
    primaryColor: string;
    secondaryColor: string;
    accentColor: string;
    backgroundColor: string;
    textColor: string;
    fontFamily: string;
    logoUrl?: string;
    borderRadius: {
        sm: string;
        md: string;
        lg: string;
    };
    shadow: {
        sm: string;
        md: string;
    };
    customRules?: string;
}
export declare function getBrandContext(orgId: string): Promise<BrandContext>;
export declare function saveBrandContext(orgId: string, brand: BrandContext): Promise<void>;
export declare function formatBrandContextForPrompt(brand: BrandContext): string;
