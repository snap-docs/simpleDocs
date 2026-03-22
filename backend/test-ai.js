import dotenv from 'dotenv';
dotenv.config();

const apiKey = process.env.OPENROUTER_API_KEY?.trim();

async function runFullTest() {
    console.log("🚀 Starting Full Diagnostic...");
    const overallStart = Date.now();

    if (!apiKey) {
        console.error("❌ Error: OPENROUTER_API_KEY is missing from .env");
        return;
    }

    // --- STEP 1: KEY VERIFICATION ---
    const keyStart = Date.now();
    try {
        const keyCheck = await fetch("https://openrouter.ai/api/v1/auth/key", {
            method: "GET",
            headers: { "Authorization": `Bearer ${apiKey}` }
        });
        const keyData = await keyCheck.json();
        const keyTime = ((Date.now() - keyStart) / 1000).toFixed(2);

        console.log(`✅ Key Verified (${keyTime}s)`);
        console.log(`   Tier: ${keyData.limit === null ? "Paid" : "Free"}`);
    } catch (e) {
        console.log("❌ Key Check Failed");
    }

    // --- STEP 2: AI COMPLETION ---
    console.log("\n⏳ Sending AI Request (Waiting for a provider to wake up)...");
    const aiStart = Date.now();

    try {
        const response = await fetch("https://openrouter.ai/api/v1/chat/completions", {
            method: "POST",
            headers: {
                "Authorization": `Bearer ${apiKey}`,
                "Content-Type": "application/json",
                "HTTP-Referer": "http://localhost:3000",
                "X-Title": "SnapDocs"
            },
            body: JSON.stringify({
                models: [
                    "google/gemini-2.0-flash-exp:free",
                    "qwen/qwen-2.5-coder-32b-instruct:free",
                    "meta-llama/llama-3.3-70b-instruct:free"
                ],
                messages: [{ role: "user", content: "Say 'Ready!'" }]
            })
        });

        const data = await response.json();
        const aiTime = ((Date.now() - aiStart) / 1000).toFixed(2);

        if (response.ok) {
            console.log(`✅ SUCCESS! (AI Response Time: ${aiTime} seconds)`);
            console.log("🤖 AI says: " + data.choices[0].message.content);
            console.log(`📡 Model used: ${data.model}`);
        } else {
            console.error(`❌ AI Error (${aiTime}s): ${data.error?.message || "Provider Error"}`);
            console.log("   (Note: 'Provider Error' on free tier usually means server congestion.)");
        }

    } catch (error) {
        console.error(`❌ Network Error: ${error.message}`);
    }

    const totalTime = ((Date.now() - overallStart) / 1000).toFixed(2);
    console.log(`\n=========================================`);
    console.log(`🏁 Total Diagnostic Time: ${totalTime} seconds`);
    console.log(`=========================================`);
}

runFullTest();